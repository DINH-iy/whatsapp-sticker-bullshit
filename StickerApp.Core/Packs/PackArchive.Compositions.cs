using System.Text.Json;

namespace StickerApp.Core;

public static partial class PackArchive
{
    private static void ValidateComposition(byte[] bytes)
    {
        try
        {
            var composition = JsonSerializer.Deserialize<LayerComposition>(bytes)
                ?? throw new InvalidDataException("The editable composition is empty.");
            composition.Validate();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The editable composition is damaged or unsupported.", exception);
        }
    }
}
