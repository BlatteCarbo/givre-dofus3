namespace GivreClone.Engine.Recovery;

public enum ItemBind
{
    None,
    Character,
    Account,
    TimedLock,
}

/// <summary>Liaison d'un exemplaire, lue sur ses effets : 981 « Lié au personnage », 982 « Lié au compte »,
/// 983 « Échangeable : date ». Un objet lié ne se forgemage pas et ne quitte pas le perso ; une date le retient seulement.</summary>
public static class ItemBindingRules
{
    public const int LinkedToCharacter = 981;
    public const int LinkedToAccount = 982;
    public const int TradableFromDate = 983;

    public static ItemBind Classify(IEnumerable<int> effectIds)
    {
        var bind = ItemBind.None;
        foreach (int id in effectIds)
        {
            if (id == LinkedToCharacter) return ItemBind.Character;
            if (id == LinkedToAccount) bind = ItemBind.Account;
            else if (id == TradableFromDate && bind == ItemBind.None) bind = ItemBind.TimedLock;
        }
        return bind;
    }

    public static bool CanForge(ItemBind bind) => bind is ItemBind.None or ItemBind.TimedLock;
    public static bool CanTrade(ItemBind bind) => bind == ItemBind.None;
    public static bool CanCrush(ItemBind bind) => bind is ItemBind.None or ItemBind.TimedLock;
    public static bool CanBank(ItemBind bind) => bind != ItemBind.Character;
    public static bool IsShownLink(int effectId) => effectId is LinkedToCharacter or LinkedToAccount;

    public static string Label(ItemBind bind) => bind switch
    {
        ItemBind.Character => "lié au personnage",
        ItemBind.Account => "lié au compte",
        ItemBind.TimedLock => "pas encore échangeable",
        _ => "",
    };
}
