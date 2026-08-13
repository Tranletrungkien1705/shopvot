using System.Collections.Generic;

internal class Order
{
	public string Id { get; set; } = "";


	public string Name { get; set; } = "";


	public string Email { get; set; } = "";


	public string Phone { get; set; } = "";


	public string Note { get; set; } = "";


	public string Source { get; set; } = "web";


	public List<OrderLine> Items { get; set; } = new List<OrderLine>();


	public long Total { get; set; }

	public string Status { get; set; } = "pending";


	public string Ts { get; set; } = "";


	public string ConfirmedTs { get; set; } = "";

}
