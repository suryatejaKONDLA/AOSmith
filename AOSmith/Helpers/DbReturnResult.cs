namespace AOSmith.Helpers
{
    public class DbReturnResult
    {
        public int ResultVal { get; set; }
        public string ResultType { get; set; }
        public string ResultMessage { get; set; }
        public string ReturnPassword { get; set; }

        public bool IsSuccess => ResultVal > 0 && ResultType?.ToLower() == "success";
    }
}
