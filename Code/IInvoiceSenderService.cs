using System.Collections.Generic;
using Invoice.Models;
using Invoices.Models;
using Microsoft.Extensions.Logging;
using TaxCollectData;

namespace Invoice.Code
{
    public interface IInvoiceSenderService
    {
        InvoiceApiHelperResponse SendInvoice(Invoices.Models.Invoice invoice);
        List<InvoiceApiHelperResponse> SendInvoices(List<Invoices.Models.Invoice> invoices, Profile profile);
        InvoiceApiHelperResponse? GetLastStatus(Invoices.Models.Invoice invoice);
    }

    public class InvoiceSenderService : IInvoiceSenderService
    {
        private readonly IProfileSecretProtector? _secretProtector;
        private readonly ILogger<InvoiceSenderService>? _logger;

        public InvoiceSenderService(IProfileSecretProtector? secretProtector = null, ILogger<InvoiceSenderService>? logger = null)
        {
            _secretProtector = secretProtector;
            _logger = logger;
        }

        public InvoiceApiHelperResponse SendInvoice(Invoices.Models.Invoice invoice)
        {
            var helper = new InvoiceApiHelper(_secretProtector, _logger);
            return helper.SendInvoice(invoice);
        }

        public List<InvoiceApiHelperResponse> SendInvoices(List<Invoices.Models.Invoice> invoices, Profile profile)
        {
            var helper = new InvoiceApiHelper(_secretProtector, _logger);
            return helper.SendInvoices(invoices, profile);
        }

        public InvoiceApiHelperResponse? GetLastStatus(Invoices.Models.Invoice invoice)
        {
            var helper = new InvoiceApiHelper(_secretProtector, _logger);
            return helper.GetLastStatus(invoice);
        }
    }
}
