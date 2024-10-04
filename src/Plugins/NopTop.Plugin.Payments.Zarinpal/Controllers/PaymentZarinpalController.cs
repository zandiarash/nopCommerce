using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Stores;
using Nop.Web.Framework.Controllers;
using System.Text;
using System.Globalization;
using Nop.Web;
using Nop.Services.Messages;
using NopTop.Plugin.Payments.Zarinpal;
using NopTop.Plugin.Payments.Zarinpal.Models;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Framework;
using Nop.Services.Security;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.Controllers
{
    public class PaymentZarinPalController : BasePaymentController
    {
        #region Fields
        private readonly IPaymentService _paymentService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IPermissionService _permissionService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly INotificationService _notificationService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly ZarinpalPaymentSettings _ZarinPalPaymentSettings;

        #endregion

        #region Ctor
        public PaymentZarinPalController(IGenericAttributeService genericAttributeService,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentService paymentService,
            IPaymentPluginManager paymentPluginManager,
            IPermissionService permissionService,
            ILocalizationService localizationService,
            ILogger logger,
            INotificationService notificationService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            IWorkContext workContext,
            ShoppingCartSettings shoppingCartSettings,
            ZarinpalPaymentSettings ZarinPalPaymentSettings)
        {
            _genericAttributeService = genericAttributeService;
            _paymentService = paymentService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentPluginManager = paymentPluginManager;
            _permissionService = permissionService;
            _localizationService = localizationService;
            _logger = logger;
            _notificationService = notificationService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _workContext = workContext;
            _shoppingCartSettings = shoppingCartSettings;
            _ZarinPalPaymentSettings = ZarinPalPaymentSettings;
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.ADMIN)]
        public async Task<IActionResult> Configure()
        {
            if (!(await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods)))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var ZarinPalPaymentSettings = await _settingService.LoadSettingAsync<ZarinpalPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                UseSandbox = ZarinPalPaymentSettings.UseSandbox,
                MerchantID = ZarinPalPaymentSettings.MerchantID,
                BlockOverseas = ZarinPalPaymentSettings.BlockOverseas,
                RialToToman = ZarinPalPaymentSettings.RialToToman,
                Method = ZarinPalPaymentSettings.Method,
                UseZarinGate = ZarinPalPaymentSettings.UseZarinGate,
                ZarinGateType = ZarinPalPaymentSettings.ZarinGateType
            };

            if (storeScope <= 0)
                return View("~/Plugins/NopTop.Payments.ZarinPal/Views/Configure.cshtml", model);

            model.UseSandbox_OverrideForStore = await _settingService.SettingExistsAsync(ZarinPalPaymentSettings, x => x.UseSandbox, storeScope);
            model.MerchantID_OverrideForStore = await _settingService.SettingExistsAsync(ZarinPalPaymentSettings, x => x.MerchantID, storeScope);
            model.BlockOverseas_OverrideForStore = await _settingService.SettingExistsAsync(ZarinPalPaymentSettings, x => x.BlockOverseas, storeScope);
            model.RialToToman_OverrideForStore = await _settingService.SettingExistsAsync(ZarinPalPaymentSettings, x => x.RialToToman, storeScope);
            model.Method_OverrideForStore = await _settingService.SettingExistsAsync(ZarinPalPaymentSettings, x => x.Method, storeScope);
            model.UseZarinGate_OverrideForStore = await _settingService.SettingExistsAsync(ZarinPalPaymentSettings, x => x.UseZarinGate, storeScope);
            model.ZarinGateType_OverrideForStore = await _settingService.SettingExistsAsync(ZarinPalPaymentSettings, x => x.ZarinGateType, storeScope);

            return View("~/Plugins/NopTop.Payments.ZarinPal/Views/Configure.cshtml", model);
        }


        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.ADMIN)]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!(await (_permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var ZarinPalPaymentSettings = await _settingService.LoadSettingAsync<ZarinpalPaymentSettings>(storeScope);

            //save settings
            ZarinPalPaymentSettings.UseSandbox = model.UseSandbox;
            ZarinPalPaymentSettings.MerchantID = model.MerchantID;
            ZarinPalPaymentSettings.BlockOverseas = model.BlockOverseas;
            ZarinPalPaymentSettings.RialToToman = model.RialToToman;
            ZarinPalPaymentSettings.Method = model.Method;
            ZarinPalPaymentSettings.UseZarinGate = model.UseZarinGate;
            ZarinPalPaymentSettings.ZarinGateType = model.ZarinGateType;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            await _settingService.SaveSettingOverridablePerStoreAsync(ZarinPalPaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(ZarinPalPaymentSettings, x => x.MerchantID, model.MerchantID_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(ZarinPalPaymentSettings, x => x.BlockOverseas, model.MerchantID_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(ZarinPalPaymentSettings, x => x.RialToToman, model.RialToToman_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(ZarinPalPaymentSettings, x => x.Method, model.Method_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(ZarinPalPaymentSettings, x => x.UseZarinGate, model.UseZarinGate_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(ZarinPalPaymentSettings, x => x.ZarinGateType, model.ZarinGateType_OverrideForStore, storeScope, false);


            //now clear settings cache
            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }

        #endregion

        public async Task<IActionResult> ResultHandler(string Status, string Authority, string OGUID)
        {
            if (await _paymentPluginManager.LoadPluginBySystemNameAsync("NopTop.Payments.Zarinpal") is not ZarinPalPaymentProcessor processor || !_paymentPluginManager.IsPluginActive(processor))
                throw new NopException("ZarinPal module cannot be loaded");

            Guid orderNumberGuid = Guid.Empty;
            try
            {
                orderNumberGuid = new Guid(OGUID);
            }
            catch { }

            var order = await _orderService.GetOrderByGuidAsync(orderNumberGuid);

            var total = Convert.ToInt32(Math.Round(order.OrderTotal, 2));
            if (_ZarinPalPaymentSettings.RialToToman)
                total = total / 10;

            if (string.IsNullOrEmpty(Status) == false && string.IsNullOrEmpty(Authority) == false)
            {
                string _refId = "0";
                System.Net.ServicePointManager.Expect100Continue = false;
                int _status = -1;
                var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
                var _ZarinPalSettings = await _settingService.LoadSettingAsync<ZarinpalPaymentSettings>(storeScope);

                var _url = $"https://{(_ZarinPalPaymentSettings.UseSandbox ? "sandbox" : "payment")}.zarinpal.com/pg/v4/payment/verify.json";
                var _values = new
                {
                    merchant_id = _ZarinPalSettings.MerchantID,
                    amount = total.ToString(),  //Toman
                    authority = Authority
                };

                var content = new StringContent(JsonConvert.SerializeObject(_values), Encoding.UTF8, "application/json");
                var _response = ZarinPalPaymentProcessor.clientZarinPal.PostAsync(_url, content).Result;
                var _responseString = _response.Content.ReadAsStringAsync().Result;

                RestVerifyModel<List<string>> restVerifyModelOK = null;
                RestVerifyModel<ErrorDetails> restVerifyModelNOK = null;
                StatusToResult resultMessage = null;

                if (Status == "OK")
                {
                    restVerifyModelOK = JsonConvert.DeserializeObject<RestVerifyModel<List<string>>>(_responseString);
                    _status = restVerifyModelOK.data.code;
                    _refId = restVerifyModelOK.data.ref_id.ToString();
                }
                else if (Status == "NOK")
                {
                    restVerifyModelNOK = JsonConvert.DeserializeObject<RestVerifyModel<ErrorDetails>>(_responseString);
                    _status = restVerifyModelNOK.errors.code;
                }

                resultMessage = ZarinpalHelper.StatusToMessage(_status);

                var orderNote = new OrderNote()
                {
                    OrderId = order.Id,
                    Note = string.Concat("پرداخت ", resultMessage.IsOk ? "" : "نا", "موفق", " - ", "پیام درگاه : ", resultMessage.Message,
                      resultMessage.IsOk ? string.Concat(" - ", "کد پی گیری : ", _refId) : ""
                    ),
                    DisplayToCustomer = true,
                    CreatedOnUtc = DateTime.UtcNow
                };

                await _orderService.InsertOrderNoteAsync(orderNote);

                if (resultMessage.IsOk && _orderProcessingService.CanMarkOrderAsPaid(order))
                {
                    order.AuthorizationTransactionId = _refId;
                    await _orderService.UpdateOrderAsync(order);
                    await _orderProcessingService.MarkOrderAsPaidAsync(order);
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                }
            }
            return RedirectToRoute("orderdetails", new { orderId = order.Id });
        }
        public ActionResult ErrorHandler(string Error)
        {
            int code = 0;
            Int32.TryParse(Error, out code);
            if (code != 0)
                Error = ZarinpalHelper.StatusToMessage(code).Message;
            ViewBag.Err = string.Concat("خطا : ", Error);
            return View("~/Plugins/NopTop.Payments.ZarinPal/Views/ErrorHandler.cshtml");
        }
    }
}