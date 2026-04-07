using System.Text.Json.Serialization;

namespace Host.Services.Warehouse;

public sealed class MoySkladStockResponse
{
    [JsonPropertyName("rows")]
    public List<MoySkladStockRow> Rows { get; set; } = [];

    [JsonPropertyName("meta")]
    public MoySkladMeta? Meta { get; set; }
}

public sealed class MoySkladMeta
{
    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}

public sealed class MoySkladStockRow
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("article")]
    public string? Article { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Actual stock quantity (физический остаток).</summary>
    [JsonPropertyName("stock")]
    public double Stock { get; set; }

    /// <summary>Reserved amount (зарезервировано).</summary>
    [JsonPropertyName("reserve")]
    public double Reserve { get; set; }

    /// <summary>In transit (ожидается поступление).</summary>
    [JsonPropertyName("inTransit")]
    public double InTransit { get; set; }

    /// <summary>Available = Stock - Reserve.</summary>
    [JsonPropertyName("quantity")]
    public double Quantity { get; set; }

    /// <summary>Cost price, in kopecks (делить на 100 для рублей).</summary>
    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("folder")]
    public MoySkladFolder? Folder { get; set; }
}

public sealed class MoySkladFolder
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("pathName")]
    public string? PathName { get; set; }
}

// ── Assortment (полный каталог, в т.ч. без движений склада) ──────────────────

public sealed class MoySkladAssortmentResponse
{
    [JsonPropertyName("rows")]
    public List<MoySkladAssortmentRow> Rows { get; set; } = [];
}

public sealed class MoySkladAssortmentMeta
{
    /// <summary>Тип объекта: "product", "variant", "service", "bundle" …</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("href")]
    public string? Href { get; set; }
}

public sealed class MoySkladAssortmentRow
{
    [JsonPropertyName("meta")]
    public MoySkladAssortmentMeta? Meta { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("article")]
    public string? Article { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("productFolder")]
    public MoySkladFolder? ProductFolder { get; set; }

    /// <summary>Папка родителя для variant — ссылается на product.</summary>
    [JsonPropertyName("product")]
    public MoySkladAssortmentParent? Product { get; set; }
}

public sealed class MoySkladAssortmentParent
{
    [JsonPropertyName("meta")]
    public MoySkladAssortmentMeta? Meta { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("productFolder")]
    public MoySkladFolder? ProductFolder { get; set; }
}
