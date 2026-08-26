namespace Invoice.Code
{
    public interface IScopedFields
    {
       
        public bool MenuOpen { get; set; }
    }

    public class ScopedFields : IScopedFields
    {
        HttpContext _httpContext;
        public ScopedFields(IHttpContextAccessor httpContext)
        {
            _httpContext = httpContext.HttpContext;
        }
        
        public bool MenuOpen { get
            {
                var mo = _httpContext.Session.GetString("menu-open");
                return mo==null?false:bool.Parse(mo);
                
            }
            set { 
                _httpContext.Session.SetString("menu-open",value.ToString());
            }}
    }


}
