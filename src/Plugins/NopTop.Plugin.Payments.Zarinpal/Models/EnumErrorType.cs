using System.ComponentModel.DataAnnotations;

namespace NopTop.Plugin.Payments.Zarinpal.Models;
public enum EnumErrorType
{
    Public,
    PaymentRequest,
    PaymentVerify,
    PaymentReverse
}
