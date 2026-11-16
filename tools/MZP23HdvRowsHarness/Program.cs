using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;

CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en-US");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fails++; }

// HdvBuyRow : l'état réagit au stock et aux achats, et notifie les bons champs.
var raised = new List<string>();
var buy = new HdvBuyRow { Gid = 1, Name = "Blé", Level = 20 };
buy.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
buy.Needed = 10;
Check("besoin 10, stock 0 -> manque", buy.IsMissing && !buy.IsCovered && buy.ToBuy == 10 && buy.BalanceText == "-10");
buy.Owned = 4;
Check("stock 4 -> reste 6, notifie OwnedText/IsMissing", buy.ToBuy == 6 && buy.OwnedText == "4" && raised.Contains("OwnedText") && raised.Contains("IsMissing"));
buy.Bought = 6;
Check("achats 6 -> exact", buy.IsExact && buy.IsCovered && buy.ToBuy == 0 && buy.BalanceText == "0");
buy.Bought = 9;
Check("achats 9 -> surplus +3", buy.IsSurplus && buy.BalanceText == "+3");
Check("niveau", buy.LevelText == "Niv. 20");
buy.Owned = 0; buy.Bought = 0; buy.PlanText = "x10 a 50";
Check("cote visible seulement si manque et plan", buy.ShowQuote);
buy.PlanText = "";
Check("cote cachée sans plan", !buy.ShowQuote);

// HdvBuyLot : OverMax suit le maximum.
var lot = new HdvBuyLot { Qty = 100, Price = 5000 };
var lotRaised = false; lot.PropertyChanged += (_, e) => lotRaised |= e.PropertyName == "OverMax";
Check("lot sans max -> pas OverMax", !lot.OverMax && lot.Label == "x100" && lot.PriceText == "5,000");
lot.Max = 4000;
Check("max 4000 < prix 5000 -> OverMax notifié", lot.OverMax && lotRaised);

// HdvScanRow
var scan = new HdvScanRow { Gid = 7, Name = "Fer", Cost = 1000, Value = 1250, Seen = 3 };
Check("gain +250 (+25.0 %)", scan.GainText == "+250" && scan.GainPctText == "+25.0 %" && scan.IsProfit && !scan.IsLoss);
Check("non douteux -> pas de revérif", !scan.IsDoubtfulProfit && !scan.CanVerify);
scan.Suspect = true;
Check("douteux -> profit douteux + revérifiable", scan.IsDoubtfulProfit && scan.CanVerify);
var loss = new HdvScanRow { Gid = 8, Cost = 500, Value = 300 };
Check("perte", loss.IsLoss && loss.GainText == "-200" && loss.GainPctText == "-40.0 %");

// HdvSizePick / HdvItemPick / HdvRelistRow / listes simples
var size = new HdvSizePick { Size = 10 };
Check("taille de lot", size.Label == "x10" && !size.Active);
var item = new HdvItemPick { Gid = 3, Name = "Orge", Owned = 1200, Sizes = new[] { size } };
Check("objet proposé", item.OwnedText == "1,200" && !item.HasIcon && item.Sizes.Count == 1);
var rel = new HdvRelistRow { Gid = 3, Qty = 100, MinPrice = 0 };
Check("relist lot", rel.LotText == "x100" && !rel.IsDry);
var step = new HdvCraftStep { Name = "Planche", Count = 4, Job = "Bucheron" };
Check("étape de craft", step.CountText == "x4" && step.JobText == "Bucheron");
var cat = new HdvCatalogPick { Name = "Pain", Level = 0, Craftable = true };
Check("catalogue sans niveau", cat.LevelText == "" && cat.Craftable);

// Persistance : les réglages achat/relist/scan survivent à un aller-retour JSON (générateur de sources, comme AppJsonContext).
var prof = new HdvProfilesFile();
var hp = new HdvProfile();
hp.Trade.BuyBudget = 5_000_000; hp.Trade.BuyT1000 = true; hp.Trade.RelistBulkPct = 88; hp.Trade.ScanMinGain = 1234; hp.Trade.NotifyReprice = true;
prof.Profiles["Perso"] = hp;
var json = JsonSerializer.Serialize(prof, typeof(HdvProfilesFile), HarnessJson.Default);
var back = (HdvProfilesFile)JsonSerializer.Deserialize(json, typeof(HdvProfilesFile), HarnessJson.Default)!;
var bt = back.Profiles["Perso"].Trade;
Check("JSON aller-retour des réglages", bt.BuyBudget == 5_000_000 && bt.BuyT1000 && bt.RelistBulkPct == 88 && bt.ScanMinGain == 1234 && bt.NotifyReprice);
var clone = hp.Clone();
clone.Trade.BuyBudget = 1;
Check("Clone() copie profondément Trade", hp.Trade.BuyBudget == 5_000_000 && clone.Trade.BuyBudget == 1);
Check("défauts de Trade", new HdvTradeSettings().BuyPricePct == 100 && new HdvTradeSettings().BuyDeductStock);

Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
return fails == 0 ? 0 : 1;

[JsonSerializable(typeof(HdvProfilesFile))]
internal sealed partial class HarnessJson : JsonSerializerContext { }
