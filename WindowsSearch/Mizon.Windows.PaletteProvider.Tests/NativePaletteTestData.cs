using Mizon.Windows.PaletteProvider;

internal static unsafe class NativePaletteTestData
{
    // Reconstructs the real Windows item factory in this diagnostic process.
    // No PaletteCatalog registration, file catalogue or registry writes occur.
    internal static nint CreateItemFactory()
    {
        nint properties = 0, map = 0, propertyFactory = 0, boxed = 0, unmarshal = 0;
        try
        {
            properties = NativeMethods.Activate("Windows.Foundation.Collections.PropertySet");
            map = NativeMethods.As(properties, new Guid("1b0d3570-0877-5ec2-8a2c-3b9539506aca"));
            propertyFactory = NativeMethods.Factory("Windows.Foundation.PropertyValue", new Guid("629bdbc8-d932-4ff4-96b9-8d96c5c1e858"));
            var value = NativeMethods.String("MizonDiagnostic");
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(propertyFactory)[18])(
                    propertyFactory, value, &boxed));
            }
            finally { NativeMethods.WindowsDeleteString(value); }

            var key = NativeMethods.String("ExtensionId");
            byte replaced = 0;
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, byte*, int>)NativeMethods.Table(map)[10])(
                    map, key, boxed, &replaced));
            }
            finally { NativeMethods.WindowsDeleteString(key); }

            unmarshal = NativeMethods.Factory("Windows.UI.Palettes.PaletteItemFactory", new Guid("ecabe149-4833-452b-8112-3eeec6d20cb0"));
            nint factory = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(unmarshal)[6])(
                unmarshal, properties, &factory));
            return factory;
        }
        finally
        {
            NativeMethods.Release(unmarshal);
            NativeMethods.Release(boxed);
            NativeMethods.Release(propertyFactory);
            NativeMethods.Release(map);
            NativeMethods.Release(properties);
        }
    }
}
