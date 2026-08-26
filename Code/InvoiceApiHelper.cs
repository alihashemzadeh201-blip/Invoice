using Invoice.Migrations;
using Invoice.Models;
using Invoices.Models;
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

        public InvoiceApiHelper()
        {

        }
        private void initApi(string memoryId, string privateKey)
        {
            MemoryId = memoryId;
            TaxApiService.Instance.Init(memoryId,
                new SignatoryConfig(privateKey, null),
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
                    throw new Exception("خطا در ارسال فاکتور! این خطا مربوط به سامانه مویان می باشد .");
                }
            }
            catch (Exception ex)
            {
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
                message = "خطا در ارسال فاکتور! این خطا مربوط به سامانه مویان می باشد .";
                status = InvoiceStatus.خطا_در_ارسال;
                responses.Add(new InvoiceApiHelperResponse(taxId, status.Value, referenceNumber, uid, message));
            }
            return responses;
        }
        public InvoiceApiHelperResponse? GetLastStatus(Invoices.Models.Invoice invoice)
        {
            initApi(invoice.Profile.MemoryId, invoice.Profile.PrivateKey);
            _api.GetServerInformation();
            getToken();
            var inquiryResultModels = new List<InquiryResultModel>();
            if (invoice.UID != null)
            {
                var uidfiscal = new UidAndFiscalId(invoice.UID, invoice.Profile.MemoryId);
                inquiryResultModels = _api.InquiryByUidAndFiscalId(new() { uidfiscal });
            }
            else
                inquiryResultModels = _api.InquiryByReferenceId(new() { invoice.ReferenceCode });
            var message = inquiryResultModels.First().Data?.ToString();
            //inquiryResultModels = _api.InquiryByTimeRange("14021001", "14021015");
            InvoiceStatus? status = null;
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
            return new InvoiceApiHelperResponse(null, status.Value, invoice.ReferenceCode, invoice.UID, message);
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
                Console.WriteLine("success login, token: " + token.Token);
            }
            else
            {
                throw new Exception("خطا در شناسه یکتا یا کلید خصوصی");
            }
        }
    }

    public record InvoiceApiHelperResponse(string TaxId, InvoiceStatus Status, string RefId, string UID, string? Response);

}
