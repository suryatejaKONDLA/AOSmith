namespace AOSmith.Models
{
    public class MailConfig
    {
        public int Mail_SNo { get; set; }
        public string Mail_From_Address { get; set; }
        public string Mail_From_Password { get; set; }
        public string Mail_Display_Name { get; set; }
        public string Mail_Host { get; set; }
        public int Mail_Port { get; set; }
        public bool Mail_SSL_Enabled { get; set; }
    }
}
