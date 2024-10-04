using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Stores;
using Nop.Services.Tax;
using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Web.Framework.Menu;
using NopTop.Plugin.Payments.Zarinpal.Models;
using NopTop.Plugin.Payments.Zarinpal.Components;
using Newtonsoft.Json;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;

namespace NopTop.Plugin.Payments.Zarinpal
{
    /// <summary>
    /// Zarinpal payment processor
    /// </summary>
    public class ZarinPalPaymentProcessor : BasePlugin, IPaymentMethod, IAdminMenuPlugin
    {
        #region Constants

        public static readonly HttpClient clientZarinPal = new HttpClient();

        #endregion

        #region Fields
        private readonly CustomerSettings _customerSettings;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IPaymentService _paymentService;
        private readonly CurrencySettings _currencySettings;
        private readonly ICurrencyService _currencyService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly ZarinpalPaymentSettings _ZarinPalPaymentSettings;
        private readonly ILanguageService _languageService;
        private readonly IStoreService _storeService;
        private readonly ICustomerService _customerService;
        private IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IAddressService _addressService;


        #endregion

        #region Ctor

        public ZarinPalPaymentProcessor(CurrencySettings currencySettings,
            IHttpContextAccessor httpContextAccessor,
            IPaymentService paymentService,
            ICurrencyService currencyService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService,
            ITaxService taxService,
            IWebHelper webHelper,
            ZarinpalPaymentSettings ZarinPalPaymentSettings,
            ILanguageService languageService,
            IStoreService storeService,
            ICustomerService customerService,
            IWorkContext workContext,
            IStoreContext storeContext,
            CustomerSettings _customerSettings,
            IAddressService addressService
            )
        {
            this._paymentService = paymentService;
            this._httpContextAccessor = httpContextAccessor;
            this._workContext = workContext;
            this._customerService = customerService;
            this._storeService = storeService;
            this._currencySettings = currencySettings;
            this._currencyService = currencyService;
            this._genericAttributeService = genericAttributeService;
            this._localizationService = localizationService;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._settingService = settingService;
            this._taxService = taxService;
            this._webHelper = webHelper;
            this._ZarinPalPaymentSettings = ZarinPalPaymentSettings;
            this._storeContext = storeContext;
            this._languageService = languageService;
            this._customerSettings = _customerSettings;
            this._addressService = addressService;
        }

        #endregion

        #region Utilities
        #endregion

        #region Methods

        Task<ProcessPaymentResult> IPaymentMethod.ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.NewPaymentStatus = PaymentStatus.Pending;
            return Task.FromResult(result);
        }

        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            Customer customer = await _customerService.GetCustomerByIdAsync(postProcessPaymentRequest.Order.CustomerId);
            Order order = postProcessPaymentRequest.Order;
            var store = await _storeService.GetStoreByIdAsync(order.StoreId);

            var total = Convert.ToInt32(Math.Round(order.OrderTotal, 2));
            if (_ZarinPalPaymentSettings.RialToToman)
                total = total / 10;

            string PhoneOfUser = string.Empty;
            var billingAddress = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
            var shippingAddress = await _addressService.GetAddressByIdAsync(order.ShippingAddressId ?? 0);

            if (_customerSettings.PhoneEnabled)// Default Phone number of the Customer
                PhoneOfUser = customer.Phone;
            if (string.IsNullOrEmpty(PhoneOfUser))//Phone number of the BillingAddress
                PhoneOfUser = billingAddress.PhoneNumber;
            if (string.IsNullOrEmpty(PhoneOfUser))//Phone number of the ShippingAddress
                PhoneOfUser = string.IsNullOrEmpty(shippingAddress?.PhoneNumber) ? PhoneOfUser : $"{PhoneOfUser} - {shippingAddress.PhoneNumber}";

            string NameFamily = $"{customer.FirstName ?? ""} {customer.LastName ?? ""}".Trim();
            string ZarinGate = _ZarinPalPaymentSettings.UseZarinGate ? _ZarinPalPaymentSettings.ZarinGateType.ToString() : null;
            string Description = $"{store.Name}{(string.IsNullOrEmpty(NameFamily) ? "" : $" - {NameFamily}")} - {customer.Email}{(string.IsNullOrEmpty(PhoneOfUser) ? "" : $" - {PhoneOfUser}")}";
            string CallbackURL = string.Concat(_webHelper.GetStoreLocation(), "Plugins/PaymentZarinpal/ResultHandler", "?OGUId=" + postProcessPaymentRequest.Order.OrderGuid);
            string StoreAddress = _webHelper.GetStoreLocation();


            //var _url = $"https://{(_ZarinPalPaymentSettings.UseSandbox ? "sandbox" : "www")}.zarinpal.com/pg/rest/WebGate/PaymentRequest.json";
            var _url = $"https://{(_ZarinPalPaymentSettings.UseSandbox ? "sandbox" : "payment")}.zarinpal.com/pg/v4/payment/request.json";

            var _requestData = new
            {
                merchant_id = _ZarinPalPaymentSettings.MerchantID,
                amount = total,
                currency = "IRT",
                callback_url = CallbackURL,
                description = Description,
                metadata = new
                {
                    mobile = PhoneOfUser,
                    email = customer.Email
                }
            };

            var _paymentRequestJsonValue = new StringContent(JsonConvert.SerializeObject(_requestData), Encoding.UTF8, "application/json");
            var _response = clientZarinPal.PostAsync(_url, _paymentRequestJsonValue).Result;
            var _responseString = _response.Content.ReadAsStringAsync().Result;

            RestRequestModel _restRequestModel =
                         JsonConvert.DeserializeObject<RestRequestModel>(_responseString);

            var uri = new Uri(ZarinpalHelper.ProduceRedirectUrl(StoreAddress,
                _restRequestModel?.data.code,
                _ZarinPalPaymentSettings.UseSandbox,
                _restRequestModel.data.authority,
                ZarinGate));

            _httpContextAccessor.HttpContext.Response.Redirect(uri.AbsoluteUri);
        }

        public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            bool hide = false;
            var storeId = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var ZarinPalPaymentSettings = await _settingService.LoadSettingAsync<ZarinpalPaymentSettings>(storeId);
            hide = string.IsNullOrWhiteSpace(_ZarinPalPaymentSettings.MerchantID);
            if (_ZarinPalPaymentSettings.BlockOverseas)
                hide = hide || ZarinpalHelper.isOverseaseIp(_httpContextAccessor.HttpContext.Connection.RemoteIpAddress);
            return hide;
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/Paymentzarinpal/Configure";
        }

        public override async Task InstallAsync()
        {
            await _settingService.SaveSettingAsync(new ZarinpalPaymentSettings
            {
                UseSandbox = true,
                MerchantID = "99999999-9999-9999-9999-999999999999",
                BlockOverseas = false,
                RialToToman = true,
                Method = EnumMethod.REST,
                UseZarinGate = false,
                ZarinGateType = EnumZarinGate.ZarinGate
            });


            string ZarinGateLink = "https://www.zarinpal.com/blog/زرین-گیت،-درگاهی-اختصاصی-به-نام-وبسایت/";

            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.Zarinpal.Fields.ZarinGate.Use"] = "Use ZarinGate",
                ["Plugins.Payments.Zarinpal.Fields.ZarinGate.Type"] = "Select ZarinGate Type",

                ["Plugins.Payments.Zarinpal.Fields.ZarinGate.Instructions"] = $"Read About the <a href=\"{ZarinGateLink}\">Zarin Gate</a> Then Select the ZarinGateLink type from below :",

                ["Plugins.Payments.ZarinPal.Fields.Method"] = "Communication Method",
                ["Plugins.Payments.ZarinPal.Fields.Method.REST"] = "REST(recommanded)",
                ["Plugins.Payments.ZarinPal.Fields.Method.SOAP"] = "SOAP(Deprecated By Zarinpal)",

                ["Plugins.Payments.ZarinPal.Fields.UseSandbox"] = "Use Snadbox for testing payment GateWay without real paying.",
                ["Plugins.Payments.ZarinPal.Fields.MerchantID"] = "GateWay Merchant ID",
                ["Plugins.Payments.ZarinPal.Instructions"] = string.Concat("You can use Zarinpal.com GateWay as a payment gateway. Zarinpal is not a bank but it is an interface which customers can pay with.",
                    "<br/>", "Please consider that if you leave MerchantId field empty the Zarinpal Gateway will be hidden and not choosable when checking out"),

                ["plugins.payments.zarinpal.PaymentMethodDescription"] = "ZarinPal, The Bank Interface",
                ["Plugins.Payments.Zarinpal.Fields.RedirectionTip"] = "You will be redirected to ZarinPal site to complete the order.",
                ["Plugins.Payments.Zarinpal.Fields.BlockOverseas"] = "Block oversease access (block non Iranians)",
                ["Plugins.Payments.Zarinpal.Fields.RialToToman"] = "Convert Rial To Toman",
                ["Plugins.Payments.Zarinpal.Fields.RialToToman.Instructions"] =
                    string.Concat(
                        "The default currency of zarinpal is Toman", "<br/>",
                        "Therefore if your website uses Rial before paying it should be converted to Toman", "<br/>",
                        "please consider that to convert Rial to Toman system divides total to 10, so the last digit will be removed", "<br/>",
                        "To do the stuff check this option"
                    )
            });

            var lang = (await _languageService.GetAllLanguagesAsync()).FirstOrDefault(x => x.LanguageCulture == "fa-IR");
            if (lang != null)
                await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
                {
                    ["Plugins.Payments.Zarinpal.Fields.ZarinGate.Use"] = "استفاده از زرین گیت",
                    ["Plugins.Payments.Zarinpal.Fields.ZarinGate.Type"] = "انتخاب نوع زرین گیت",

                    ["Plugins.Payments.Zarinpal.Fields.ZarinGate.Instructions"] = string.Concat("لطفا اول شرایط استفاده از زرین گیت را در ", $"<a href=\"{ZarinGateLink}\"> در این قسمت </a>", "مطالعه نموده و سپس نوع آن را انتخاب نمایید"),

                    ["Plugins.Payments.ZarinPal.Fields.Method"] = "روش پرداخت",

                    ["Plugins.Payments.ZarinPal.Fields.UseSandbox"] = "تست درگاه زرین پال بدون پرداخت هزینه",
                    ["Plugins.Payments.ZarinPal.Fields.MerchantID"] = "کد پذیرنده",

                    ["Plugins.Payments.ZarinPal.Instructions"] =
                        string.Concat("شما می توانید از زرین پال به عنوان یک درگاه پرداخت استفاده نمایید، زرین پال یک بانک نیست بلکه یک واسط بانکی است که کاربران میتوانند از طریق آن مبلغ مورد نظر را پرداخت نمایند، باید آگاه باشید که درگاه زرین پال درصدی از پول پرداخت شده کاربران را به عنوان کارمزد دریافت میکند.",
                        "<br/>", "توجه داشته باشید که اگر فیلد کد پذیرنده خالی باشد درگاه زرین پال در هنگام پرداخت مخفی می شود و قابل انتخاب نیست"),
                    ["plugins.payments.zarinpal.PaymentMethodDescription"] = "درگاه واسط زرین پال",
                    ["Plugins.Payments.Zarinpal.Fields.RedirectionTip"] = "هم اکنون به درگاه بانک زرین پال منتقل می شوید.",
                    ["Plugins.Payments.Zarinpal.Fields.BlockOverseas"] = "قطع دسترسی برای آی پی های خارج از کشور",
                    ["Plugins.Payments.Zarinpal.Fields.RialToToman"] = "تبدیل ریال به تومن",
                    ["Plugins.Payments.Zarinpal.Fields.RialToToman.Instructions"] =
                        string.Concat(
                            "واحد ارزی پیش فرض درگاه پرداخت زرین پال تومان می باشد.", "<br/>",
                            "لذا در صورتی که وبسایت شما از واحد ارزی ریال استفاده می کند باید قبل از پرداخت مبلغ نهایی به تومان تبدیل گردد", "<br/>",
                            "لطفا در نظر داشته باشید که جهت تبدیل ریال به تومان عدد تقسیم بر 10 شده و در واقع رقم آخر حذف می گردد", "<br/>",
                            "در صورتی که مایل به تغییر از ریال به تومان هنگام پرداخت می باشید این گزینه را فعال نمایید"
                        )
                }, languageId: lang.Id);

            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<ZarinpalPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync(new List<string>{
                "Plugins.Payments.Zarinpal.Fields.ZarinGate.Use",
                "Plugins.Payments.Zarinpal.Fields.ZarinGate.Type",
                "Plugins.Payments.Zarinpal.Fields.ZarinGate.Instructions",
                "Plugins.Payments.ZarinPal.Fields.Method",
                "Plugins.Payments.ZarinPal.Fields.Method.REST",
                "Plugins.Payments.ZarinPal.Fields.Method.SOAP",
                "Plugins.Payments.ZarinPal.Fields.UseSandbox",
                "Plugins.Payments.ZarinPal.Fields.MerchantID",
                "plugins.payments.zarinpal.PaymentMethodDescription",
                "Plugins.Payments.ZarinPal.Instructions",
                "Plugins.Payments.Zarinpal.Fields.RedirectionTip",
                "Plugins.Payments.Zarinpal.Fields.BlockOverseas",
                "Plugins.Payments.Zarinpal.Fields.RialToToman",
                "Plugins.Payments.Zarinpal.Fields.RialToToman.Instructions",
            });

            await base.UninstallAsync();
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        public Type GetPublicViewComponent()
        {
            //return "PaymentZarinpal";
            return typeof(PaymentZarinpalViewComponent);
        }

        #endregion

        #region Properties

        public bool SupportCapture
        {
            get { return false; }
        }

        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        public bool SupportRefund
        {
            get { return false; }
        }

        public bool SupportVoid
        {
            get { return false; }
        }

        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("plugins.payments.zarinpal.PaymentMethodDescription");
        }

        #endregion
        public Task ManageSiteMapAsync(SiteMapNode rootNode)
        {
            var nopTopPluginsNode = rootNode.ChildNodes.FirstOrDefault(x => x.SystemName == "NopTop");
            if (nopTopPluginsNode == null)
            {
                nopTopPluginsNode = new SiteMapNode()
                {
                    SystemName = "NopTop",
                    Title = "NopTop",
                    Visible = true,
                    IconClass = "fa-gear"
                };
                rootNode.ChildNodes.Add(nopTopPluginsNode);
            }

            var menueLikeProduct = new SiteMapNode()
            {
                SystemName = "ZarinPal",
                Title = "ZarinPal Configuration",
                ControllerName = "PaymentZarinPal",
                ActionName = "Configure",
                Visible = true,
                IconClass = "fa-dot-circle-o",
                RouteValues = new RouteValueDictionary() { { "Area", "Admin" } },
            };

            nopTopPluginsNode.ChildNodes.Add(menueLikeProduct);
            return Task.CompletedTask;
        }

        public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult<decimal>(0);
        }

        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return Task.FromResult(result);
        }

        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return Task.FromResult(result);
        }

        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return Task.FromResult(result);
        }

        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException("order");

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }
    }
}
