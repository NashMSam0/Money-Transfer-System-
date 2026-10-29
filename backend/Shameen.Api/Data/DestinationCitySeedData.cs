using Microsoft.EntityFrameworkCore;
using Shameen.Api.Models;

namespace Shameen.Api.Data;

public static class DestinationCitySeedData
{
    private static readonly (string Country, string City)[] Cities =
    [
        ("Zambia", "Lusaka"), ("Zambia", "Kitwe"), ("Zambia", "Ndola"), ("Zambia", "Livingstone"), ("Zambia", "Kabwe"), ("Zambia", "Chipata"), ("Zambia", "Solwezi"), ("Zambia", "Chingola"), ("Zambia", "Mufulira"), ("Zambia", "Kasama"),
        ("South Africa", "Johannesburg"), ("South Africa", "Cape Town"), ("South Africa", "Durban"), ("South Africa", "Pretoria"), ("South Africa", "Gqeberha"), ("South Africa", "Bloemfontein"), ("South Africa", "Polokwane"), ("South Africa", "Mbombela"), ("South Africa", "Kimberley"), ("South Africa", "East London"), ("South Africa", "Rustenburg"),
        ("Tanzania", "Dar es Salaam"), ("Tanzania", "Dodoma"), ("Tanzania", "Arusha"), ("Tanzania", "Mwanza"), ("Tanzania", "Zanzibar City"), ("Tanzania", "Mbeya"), ("Tanzania", "Morogoro"), ("Tanzania", "Tanga"), ("Tanzania", "Moshi"), ("Tanzania", "Iringa"), ("Tanzania", "Tabora"), ("Tanzania", "Kigoma"),
        ("Kenya", "Nairobi"), ("Kenya", "Mombasa"), ("Kenya", "Kisumu"), ("Kenya", "Nakuru"), ("Kenya", "Eldoret"), ("Kenya", "Thika"),
        ("Zimbabwe", "Harare"), ("Zimbabwe", "Bulawayo"), ("Zimbabwe", "Mutare"), ("Zimbabwe", "Gweru"), ("Zimbabwe", "Kwekwe"), ("Zimbabwe", "Masvingo"), ("Zimbabwe", "Chitungwiza"),
        ("Uganda", "Kampala"), ("Uganda", "Entebbe"), ("Uganda", "Jinja"), ("Uganda", "Mbarara"), ("Uganda", "Gulu"), ("Uganda", "Mbale"),
        ("United Kingdom", "London"), ("United Kingdom", "Birmingham"), ("United Kingdom", "Manchester"), ("United Kingdom", "Glasgow"), ("United Kingdom", "Leeds"), ("United Kingdom", "Liverpool"), ("United Kingdom", "Bristol")
    ];

    public static async Task EnsureAsync(TransferDbContext db)
    {
        var existingCities = await db.DestinationCities.AsNoTracking().Select(c => new { c.Country, c.City }).ToListAsync();
        foreach (var (country, city) in Cities)
            if (!existingCities.Any(x => x.Country == country && x.City == city))
                db.DestinationCities.Add(new DestinationCity { Country = country, City = city });
        await db.SaveChangesAsync();

        var branchCities = await db.Branches.AsNoTracking().Select(b => b.City).ToListAsync();
        foreach (var city in Cities.Select(x => x.City).Distinct())
            if (!branchCities.Contains(city, StringComparer.OrdinalIgnoreCase))
                db.Branches.Add(new Branch { Name = $"Shameen {city}", City = city });
        await db.SaveChangesAsync();
    }
}
