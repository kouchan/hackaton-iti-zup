namespace Hackaiti.CheckoutService.Worker.Model
{
    public class CartTotal
    {
        public long Amount { get; set; }
        public long Scale { get; set; }
        public string CurrencyCode { get; set; }
    }
}