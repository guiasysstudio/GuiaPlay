using System.Runtime.InteropServices;

namespace GuiaPlay.App.Services;

internal static partial class ShellAssociationNotifier
{
    private const uint AssociationChanged = 0x08000000;
    private const uint IdList = 0x0000;

    public static void NotifyChanged() => SHChangeNotify(AssociationChanged, IdList, 0, 0);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
