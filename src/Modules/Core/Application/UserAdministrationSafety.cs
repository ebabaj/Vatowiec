namespace Vatowiec.Core.Application;

public static class UserAdministrationSafety
{
    public static void ValidateRoleRemoval(Guid actor, Guid target, bool targetIsActiveAdministrator, int activeAdministrators)
    {
        if (actor == target) throw new ArgumentException("Nie można odebrać sobie roli administratora przez ten ekran.");
        if (targetIsActiveAdministrator && activeAdministrators <= 1) throw new ArgumentException("Nie można odebrać roli ostatniemu aktywnemu administratorowi.");
    }
    public static void ValidateBlock(Guid actor, Guid target, bool targetIsAdministrator, int activeAdministrators)
    {
        if (actor == target) throw new ArgumentException("Nie można zablokować własnego konta.");
        if (targetIsAdministrator && activeAdministrators <= 1) throw new ArgumentException("Nie można zablokować ostatniego aktywnego administratora.");
    }
}
