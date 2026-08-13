using System.Collections.Generic;

internal record OrderReq(string name, string email, string phone, string? note, string? source, List<OrderItem> items);
