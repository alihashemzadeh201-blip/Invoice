using Invoice.Code;
using Invoice.Migrations;
using Invoice.Models;
using Invoices.Models;
using Microsoft.Extensions.Logging;
using Nancy.Json;
using SQLitePCL;
using System;
using System.IdentityModel.Tokens.Jwt;
using TaxCollectData.Library.Abstraction;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;

namespace TaxCollectData
{
    public class InvoiceApiHelper
    {
        public string MemoryId { get; set; }
        private readonly ITaxIdGenerator _taxIdGenerator = new TaxIdGenerator(new VerhoffProvider());
        private ITaxApis _api;
        private readonly IProfileSecretProtector? _secretProtector;
        private readonly ILogger? _logger;

        public InvoiceApiHelper(IProfileSecretProtector? secretProtector = null, ILogger? logger = null)
        {
            _secretProtector = secretProtector;
            _logger = logger;
        }
        private void initApi(string memoryId, string privateKey)
        {
            MemoryId = memoryId;
            // کلید خصوصی مودی در پایگاه داده به صورت رمزنگاری شده ذخیره می‌شود
            var plainPrivateKey = _secretProtector?.Unprotect(privateKey) ?? privateKey;
            TaxApiService.Instance.Init(memoryId,
                new SignatoryConfig(plainPrivateKey, null),
                new NormalProperties(ClientType.SELF_TSP), "https://tp.tax.gov.ir/req/api");
            _api = TaxApiService.Instance.TaxApis;
        }
        public InvoiceApiHelperResponse SendInvoice(Invoices.Models.Invoice invoice)
        {

            string? message = "";
            InvoiceStatus? status = null;
            string referenceNumber = null;
            string uid = null;
            string taxId = null;

            try
            {

                if (invoice.Profile?.PrivateKey == null || invoice.Profile?.MemoryId == null)
                {
                    throw new Exception("کلید خصوصی یا شناسه یکتای حافظه مالیاتی وارد نشده است. لطفا با مراجعه به ویرایش پروفایل نسبت به ثبت آنها اقدام نمایید.");
                }
                initApi(invoice.Profile.MemoryId, invoice.Profile.PrivateKey);
                var invoices = new List<InvoiceDto> { create_invoice(invoice) };
                taxId = invoices.First().Header.Taxid;
                _api.GetServerInformation();
                try
                {
                    getToken();

                    var inv = invoices.First();


                    var responseModel = _api.SendInvoices(invoices, null);
                    var inquiryResultModels = new List<InquiryResultModel>();
                    if (responseModel?.Body?.Result != null && responseModel.Body.Result.Any())
                    {
                        message = responseModel.Body.Result.First().ToString();
                        var packetResponse = responseModel.Body.Result.First();
                        referenceNumber = packetResponse.ReferenceNumber;
                        uid = packetResponse.Uid;

                        Thread.Sleep(5000);
                        var uidfiscal = new UidAndFiscalId(uid, invoice.Profile.MemoryId);
                        inquiryResultModels = _api.InquiryByUidAndFiscalId(new() { uidfiscal });

                        message = inquiryResultModels.First().Data?.ToString();

                        if (inquiryResultModels.Count > 0)
                        {
                            switch (inquiryResultModels[0].Status.ToLower())
                            {
                                case "success":
                                    status = InvoiceStatus.موفق;
                                    break;
                                case "failed":
                                    status = InvoiceStatus.اشکال_در_صورتحساب;
                                    break;
                                case "pending":
                                    status = InvoiceStatus.در_حال_ارسال;
                                    break;
                                case "in_progress":
                                    status = InvoiceStatus.در_حال_انجام;
                                    break;
                                case "not_found":
                                    status = InvoiceStatus.پیدا_نشد;
                                    break;
                            }
                        }
                    }
                    else
                    {
                        message = responseModel.Body?.Errors.FirstOrDefault()?.Detail;
                        status = InvoiceStatus.خطا_در_ارسال;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "خطا در ارتباط با سامانه مویان هنگام ارسال صورتحساب {TaxId}", taxId);
                    throw new Exception("خطا در ارسال فاکتور! این خطا مربوط به سامانه مویان می باشد .");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "خطا در ارسال صورتحساب {TaxId}", taxId);
                message = ex.Message;
                status = InvoiceStatus.خطا_در_ارسال;
            }
            return new InvoiceApiHelperResponse(taxId, status.Value, referenceNumber, uid, message);
        }
        public List<InvoiceApiHelperResponse> SendInvoices(List<Invoices.Models.Invoice> factors, Profile profile)
        {

            string? message = "";
            InvoiceStatus? status = null;
            string referenceNumber = null;
            string uid = null;
            string taxId = null;
            var responses = new List<InvoiceApiHelperResponse>();
            try
            {
                initApi(profile.MemoryId, profile.PrivateKey);
                var invoices = new List<InvoiceDto>();
                foreach (var item in factors)
                {
                    invoices.Add(create_invoice(item));
                }
                taxId = invoices.First().Header.Taxid;
                _api.GetServerInformation();
                getToken();
                if (profile?.PrivateKey == null || profile?.MemoryId == null)
                {
                    message = "کلید خصوصی یا شناسه یکتای حافظه مالیاتی وارد نشده است. لطفا با مراجعه به ویرایش پروفایل نسبت به ثبت آنها اقدام نمایید.";
                    status = InvoiceStatus.خطا_در_ارسال;
                    responses.Add(new InvoiceApiHelperResponse(taxId, status.Value, referenceNumber, uid, message));
                    return responses;
                }

                var responseModel = _api.SendInvoices(invoices, null);
                var inquiryResultModels = new List<InquiryResultModel>();
                Thread.Sleep(5000);
                if (responseModel?.Body?.Result != null && responseModel.Body.Result.Any())
                {
                    var uidfiscalList = new List<UidAndFiscalId>(); ;
                    foreach (var item in responseModel.Body.Result)
                    {
                        var packetResponse = item;
                        uid = packetResponse.Uid;
                        //Thread.Sleep(5000);
                        uidfiscalList.Add(new UidAndFiscalId(uid, profile.MemoryId));
                    }
                    inquiryResultModels = _api.InquiryByUidAndFiscalId(uidfiscalList);

                    for (int i = 0; i < responseModel.Body.Result.Count; i++)
                    {
                        var packetResponse = responseModel.Body.Result.ElementAt(i);
                        message = packetResponse.ToString();
                        referenceNumber = packetResponse.ReferenceNumber;
                        uid = packetResponse.Uid;

                        //Thread.Sleep(5000);

                        message = inquiryResultModels[i].Data?.ToString();

                        switch (inquiryResultModels[i].Status.ToLower())
                        {
                            case "success":
                                status = InvoiceStatus.موفق;
                                break;
                            case "failed":
                                status = InvoiceStatus.اشکال_در_صورتحساب;
                                break;
                            case "pending":
                                status = InvoiceStatus.در_حال_ارسال;
                                break;
                            case "in_progress":
                                status = InvoiceStatus.در_حال_انجام;
                                break;
                            case "not_found":
                                status = InvoiceStatus.پیدا_نشد;
                                break;
                        }

                        responses.Add(new InvoiceApiHelperResponse(taxId, status.Value, referenceNumber, uid, message));
                    }


                }
                else
                {
                    message = "خطایی در دریافت پاسخ رخ داده است.";
                    status = InvoiceStatus.خطا_در_ارسال;
                    responses.Add(new InvoiceApiHelperResponse(taxId, status.Value, referenceNumber, uid, message));

                }

            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "خطا در ارسال دسته‌ای صورتحساب‌ها برای شناسه حافظه {MemoryId}", profile?.MemoryId);
                message = "خطا در ارسال فاکتور! این خطا مربوط به سامانه مویان می باشد .";
                status = InvoiceStatus.خطا_در_ارسال;
                responses.Add(new InvoiceApiHelperResponse(taxId, status.Value, referenceNumber, uid, message));
            }
            _logger?.LogInformation("ارسال دسته‌ای {Count} صورتحساب برای شناسه حافظه {MemoryId} انجام شد.", factors.Count, profile?.MemoryId);
            return responses;
        }
        public InvoiceApiHelperResponse? GetLastStatus(Invoices.Models.Invoice invoice)
        {
            if (invoice?.Profile == null)
            {
                return new InvoiceApiHelperResponse(invoice?.Taxid, invoice?.Status ?? InvoiceStatus.خطا_در_ارسال, invoice?.ReferenceCode, invoice?.UID, "پروفایل صادرکننده مشخص نیست.");
            }

            if (string.IsNullOrWhiteSpace(invoice.UID) && string.IsNullOrWhiteSpace(invoice.ReferenceCode))
            {
                return new InvoiceApiHelperResponse(invoice.Taxid, invoice.Status ?? InvoiceStatus.در_انتظار_ارسال, null, null, "صورتحساب هنوز به سامانه مودیان ارسال نشده است و فاقد شناسه رهگیری می‌باشد.");
            }

            try
            {
                initApi(invoice.Profile.MemoryId, invoice.Profile.PrivateKey);
                _api.GetServerInformation();
                getToken();
                var inquiryResultModels = new List<InquiryResultModel>();
                if (!string.IsNullOrWhiteSpace(invoice.UID))
                {
                    var uidfiscal = new UidAndFiscalId(invoice.UID, invoice.Profile.MemoryId);
                    inquiryResultModels = _api.InquiryByUidAndFiscalId(new() { uidfiscal });
                }
                else if (!string.IsNullOrWhiteSpace(invoice.ReferenceCode))
                {
                    inquiryResultModels = _api.InquiryByReferenceId(new() { invoice.ReferenceCode });
                }

                if (inquiryResultModels == null || inquiryResultModels.Count == 0)
                {
                    return new InvoiceApiHelperResponse(invoice.Taxid, invoice.Status ?? InvoiceStatus.خطا_در_ارسال, invoice.ReferenceCode, invoice.UID, "پاسخی از سامانه مودیان دریافت نشد.");
                }

                var message = inquiryResultModels[0].Data?.ToString();
                InvoiceStatus status = invoice.Status ?? InvoiceStatus.در_حال_ارسال;
                var rawStatus = inquiryResultModels[0].Status?.ToLower() ?? "";
                switch (rawStatus)
                {
                    case "success":
                        status = InvoiceStatus.موفق;
                        break;
                    case "failed":
                        status = InvoiceStatus.اشکال_در_صورتحساب;
                        break;
                    case "pending":
                        status = InvoiceStatus.در_حال_ارسال;
                        break;
                    case "in_progress":
                        status = InvoiceStatus.در_حال_انجام;
                        break;
                    case "not_found":
                        status = InvoiceStatus.پیدا_نشد;
                        break;
                }
                return new InvoiceApiHelperResponse(invoice.Taxid, status, invoice.ReferenceCode, invoice.UID, message);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "خطا در استعلام آخرین وضعیت صورتحساب {InvoiceId}", invoice.Id);
                return new InvoiceApiHelperResponse(invoice.Taxid, invoice.Status ?? InvoiceStatus.خطا_در_ارسال, invoice.ReferenceCode, invoice.UID, "خطا در استعلام از سامانه مودیان: " + ex.Message);
            }
        }

        private InvoiceDto create_invoice(Invoices.Models.Invoice invoice)
        {
            var random = new Random();
            long randomSerialDecimal = random.Next(999999999);
            var now = new DateTimeOffset(DateTime.Now);
            var date = invoice.Indatim.Value;
            date = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, DateTimeKind.Local);
            var sodoor = new DateTimeOffset(date);
            var taxId = _taxIdGenerator.GenerateTaxId(MemoryId, randomSerialDecimal, invoice.Indatim.Value);
            var header = new InvoiceHeaderDto
            {
                Indati2m = now.ToUnixTimeMilliseconds(),
                Indatim = sodoor.ToUnixTimeMilliseconds(),
                Taxid = taxId
            };
            Utils.CopyProperties<InvoiceHeaderDto>(invoice, ref header, "Indati2m", "Taxid");
            var bodyList = new List<InvoiceBodyDto>();
            foreach (var item in invoice.InvoiceItems)
            {
                var body = new InvoiceBodyDto();
                Utils.CopyProperties<InvoiceBodyDto>(item, ref body);
                bodyList.Add(body);
            }
            var payments = new List<PaymentDto>();
            foreach (var item in invoice.Payments)
            {
                var payment = new PaymentDto();
                Utils.CopyProperties<PaymentDto>(item, ref payment);
                payments.Add(payment);
            }

            return new InvoiceDto
            {
                Header = header,
                Body = bodyList,
                Payments = payments
            };
        }
        private void getToken()
        {
            var token = _api.RequestToken();
            if (token?.Token != null && token.Token.Length > 0)
            {
                // توکن هرگز نباید در خروجی یا لاگ نوشته شود
                _logger?.LogDebug("دریافت توکن از سامانه مالیاتی با موفقیت انجام شد.");
            }
            else
            {
                throw new Exception("خطا در شناسه یکتا یا کلید خصوصی");
            }
        }
    }

    public record InvoiceApiHelperResponse(string TaxId, InvoiceStatus Status, string RefId, string UID, string? Response);

}
