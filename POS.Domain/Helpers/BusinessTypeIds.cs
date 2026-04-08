namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching BusinessTypeCatalog.Id values (seeded order).
/// </summary>
public static class BusinessTypeIds
{
    public const int Restaurant = 1;
    public const int Cafe = 2;
    public const int Bar = 3;
    public const int FoodTruck = 4;
    public const int Taqueria = 5;
    public const int Retail = 6;
    public const int Abarrotes = 7;
    public const int Ferreteria = 8;
    public const int Papeleria = 9;
    public const int Farmacia = 10;
    public const int General = 11;
    public const int Servicios = 12;

    public static int FromCode(string? code) => code?.ToLowerInvariant() switch
    {
        "restaurant" => Restaurant,
        "cafe" => Cafe,
        "bar" => Bar,
        "foodtruck" => FoodTruck,
        "taqueria" => Taqueria,
        "retail" => Retail,
        "abarrotes" => Abarrotes,
        "ferreteria" => Ferreteria,
        "papeleria" => Papeleria,
        "farmacia" => Farmacia,
        "general" => General,
        "servicios" => Servicios,
        _ => 0
    };

    public static string ToCode(int id) => id switch
    {
        Restaurant => "Restaurant",
        Cafe => "Cafe",
        Bar => "Bar",
        FoodTruck => "FoodTruck",
        Taqueria => "Taqueria",
        Retail => "Retail",
        Abarrotes => "Abarrotes",
        Ferreteria => "Ferreteria",
        Papeleria => "Papeleria",
        Farmacia => "Farmacia",
        General => "General",
        Servicios => "Servicios",
        _ => "General"
    };
}
