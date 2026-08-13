internal class MailCfg
{
	public bool enabled { get; set; }

	public string smtpHost { get; set; } = "smtp.gmail.com";


	public int smtpPort { get; set; } = 587;


	public string user { get; set; } = "";


	public string appPassword { get; set; } = "";


	public string fromEmail { get; set; } = "";


	public string fromName { get; set; } = "Shop";


	public string ownerEmail { get; set; } = "";

}
