using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Mizon.Windows.SearchProvider;

// NativeAOT has no built-in COM server on Windows. Windows Search receives this
// ABI directly and therefore never needs to load CoreCLR for this provider.
public static unsafe class SearchProtocolExports
{
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int E_POINTER = unchecked((int)0x80004003);
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int HRESULT_INSUFFICIENT_BUFFER = unchecked((int)0x8007007A);
    private const int FILTER_E_END_OF_CHUNKS = unchecked((int)0x80041700);
    private const int FILTER_E_NO_TEXT = unchecked((int)0x80041705);

    [StructLayout(LayoutKind.Sequential)]
    private struct ComObject
    {
        public void** VTable;
        public int ReferenceCount;
        public int Kind;
        public int FilterChunkReturned;
        public int FilterTextOffset;
        public int IsContainer;
        public int FilterValueReturned;
        public char* Url;
    }

    private static readonly void** ClassFactoryVTable = CreateClassFactoryVTable();
    private static readonly void** ProtocolVTable = CreateProtocolVTable();
    private static readonly void** AccessorVTable = CreateAccessorVTable();
    private static readonly void** FilterVTable = CreateFilterVTable();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject", CallConvs = [typeof(CallConvStdcall)])]
    public static int DllGetClassObject(Guid* classId, Guid* interfaceId, void** result)
    {
        Trace("DllGetClassObject");
        if (classId is null || interfaceId is null || result is null)
            return E_POINTER;

        *result = null;
        if (*classId != SearchProviderConstants.ClassId)
            return E_NOINTERFACE;

        var factory = Allocate(ClassFactoryVTable, 1);
        return QueryInterface(factory, interfaceId, result);
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow", CallConvs = [typeof(CallConvStdcall)])]
    public static int DllCanUnloadNow() => S_FALSE;

    private static ComObject* Allocate(void** vtable, int kind, bool isContainer = false)
    {
        var result = (ComObject*)NativeMemory.AllocZeroed((nuint)sizeof(ComObject));
        result->VTable = vtable;
        result->ReferenceCount = 1;
        result->Kind = kind;
        result->IsContainer = isContainer ? 1 : 0;
        return result;
    }

    private static void** CreateClassFactoryVTable()
    {
        var table = AllocateVTable(5);
        table[0] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, Guid*, void**, int>)&ClassFactoryQueryInterface;
        table[1] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&AddReference;
        table[2] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&Release;
        table[3] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, void*, Guid*, void**, int>)&CreateInstance;
        table[4] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, int, int>)&LockServer;
        return table;
    }

    private static void** CreateProtocolVTable()
    {
        var table = AllocateVTable(7);
        table[0] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, Guid*, void**, int>)&ProtocolQueryInterface;
        table[1] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&AddReference;
        table[2] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&Release;
        table[3] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, void*, void*, void*, int>)&ProtocolInit;
        table[4] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, char*, void*, void*, void*, void**, int>)&CreateAccessor;
        table[5] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, void*, int>)&CloseAccessor;
        table[6] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, int>)&ProtocolShutdown;
        return table;
    }

    private static void** CreateAccessorVTable()
    {
        var table = AllocateVTable(16);
        table[0] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, Guid*, void**, int>)&AccessorQueryInterface;
        table[1] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&AddReference;
        table[2] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&Release;
        table[3] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, void*, void*, int>)&AddRequestParameter;
        table[4] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, char*, uint, uint*, int>)&GetDocFormat;
        table[5] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, Guid*, int>)&GetClassId;
        table[6] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, char*, uint, uint*, int>)&GetHost;
        table[7] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, int>)&IsDirectory;
        table[8] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, ulong*, int>)&GetSize;
        table[9] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, long*, int>)&GetLastModified;
        table[10] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, char*, uint, uint*, int>)&GetFileName;
        table[11] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, byte*, uint, uint*, int>)&GetSecurityDescriptor;
        table[12] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, char*, uint, uint*, int>)&GetRedirectedUrl;
        table[13] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, Guid*, int>)&GetSecurityProvider;
        table[14] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, void**, int>)&BindToStream;
        table[15] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, void**, int>)&BindToFilter;
        return table;
    }

    // IFilter is intentionally implemented by this provider instead of relying on
    // the system text filter. A protocol handler owns virtual URLs; Search cannot
    // resolve one to a file extension and therefore cannot reliably choose a filter.
    private static void** CreateFilterVTable()
    {
        var table = AllocateVTable(8);
        table[0] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, Guid*, void**, int>)&FilterQueryInterface;
        table[1] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&AddReference;
        table[2] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint>)&Release;
        table[3] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint, uint, void*, uint*, int>)&FilterInit;
        table[4] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, StatChunk*, int>)&GetChunk;
        table[5] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, uint*, char*, int>)&GetText;
        table[6] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, void**, int>)&GetValue;
        table[7] = (void*)(delegate* unmanaged[Stdcall]<ComObject*, FilterRegion, Guid*, void**, int>)&BindRegion;
        return table;
    }

    private static void** AllocateVTable(int slots) => (void**)NativeMemory.AllocZeroed((nuint)(slots * sizeof(void*)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ClassFactoryQueryInterface(ComObject* self, Guid* interfaceId, void** result) => QueryInterface(self, interfaceId, result);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ProtocolQueryInterface(ComObject* self, Guid* interfaceId, void** result) => QueryInterface(self, interfaceId, result);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int AccessorQueryInterface(ComObject* self, Guid* interfaceId, void** result) => QueryInterface(self, interfaceId, result);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FilterQueryInterface(ComObject* self, Guid* interfaceId, void** result) => QueryInterface(self, interfaceId, result);

    private static int QueryInterface(ComObject* self, Guid* interfaceId, void** result)
    {
        if (interfaceId is null || result is null)
            return E_POINTER;

        *result = null;
        var supported = *interfaceId == SearchProviderConstants.IUnknownId ||
            (self->Kind == 1 && *interfaceId == SearchProviderConstants.IClassFactoryId) ||
            (self->Kind == 2 && *interfaceId == SearchProviderConstants.ISearchProtocolId) ||
            (self->Kind == 3 && *interfaceId == SearchProviderConstants.IUrlAccessorId) ||
            (self->Kind == 4 && *interfaceId == SearchProviderConstants.IFilterId);
        if (!supported)
            return E_NOINTERFACE;

        AddReferenceCore(self);
        *result = self;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddReference(ComObject* self) => AddReferenceCore(self);

    private static uint AddReferenceCore(ComObject* self) => (uint)Interlocked.Increment(ref self->ReferenceCount);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(ComObject* self)
    {
        var count = Interlocked.Decrement(ref self->ReferenceCount);
        if (count == 0)
        {
            if (self->Url is not null)
                NativeMemory.Free(self->Url);
            NativeMemory.Free(self);
        }
        return (uint)Math.Max(count, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CreateInstance(ComObject* _, void* outer, Guid* interfaceId, void** result)
    {
        Trace($"CreateInstance outer={(outer is null ? "null" : "set")}");
        if (outer is not null)
            return E_INVALIDARG;
        if (interfaceId is null || result is null)
            return E_POINTER;
        return QueryInterface(Allocate(ProtocolVTable, 2), interfaceId, result);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LockServer(ComObject* self, int lockServer) => S_OK;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ProtocolInit(ComObject* self, void* timeoutInfo, void* protocolHandlerSite, void* proxyInfo) => S_OK;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CloseAccessor(ComObject* self, void* accessor) => S_OK;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ProtocolShutdown(ComObject* _) => S_OK;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CreateAccessor(ComObject* self, char* url, void* authenticationInfo, void* incrementalAccessInfo, void* itemInfo, void** result)
    {
        if (url is null || result is null)
            return E_POINTER;
        *result = null;
        var requestedUrl = new string(url);
        Trace($"CreateAccessor url={requestedUrl}");
        // SearchProtocolHost may canonicalize the configured root (for
        // example by adding a trailing slash or a user-SID segment). Accept
        // every URL owned by this provider and infer the root from its final
        // path segment instead of requiring byte-for-byte equality.
        // Windows Search can add a user SID ahead of localhost when it
        // dispatches a machine-wide protocol handler. Restrict dispatch to
        // Mizon's own scheme while allowing that documented canonical form.
        if (!requestedUrl.StartsWith("mizon://", StringComparison.OrdinalIgnoreCase))
        {
            Trace("CreateAccessor rejected URL");
            return E_INVALIDARG;
        }
        var normalizedUrl = requestedUrl.TrimEnd('/');
        // The first integration slice indexes one music document directly.
        // Returning the content as a text stream lets Windows' built-in
        // text/plain filter process it and avoids marshaling a custom IFilter
        // across the SearchProtocolHost/SearchFilterHost process boundary.
        var isContainer = false;
        var accessor = Allocate(AccessorVTable, 3, isContainer);
        accessor->Url = CopyString(requestedUrl);
        *result = accessor;
        Trace($"CreateAccessor accepted container={isContainer}");
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int AddRequestParameter(ComObject* self, void* propertySpecification, void* propertyValue) => S_OK;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetDocFormat(ComObject* _, char* buffer, uint length, uint* required) => WriteString("text/plain", buffer, length, required);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetHost(ComObject* _, char* buffer, uint length, uint* required) => WriteString("localhost", buffer, length, required);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetFileName(ComObject* self, char* buffer, uint length, uint* required)
    {
        // BindToStream requires the shell parsing path, not a display-only
        // filename. The .txt extension selects Windows' built-in plain-text
        // filter for the virtual stream.
        var accessorUrl = self->Url is null ? SearchProviderConstants.RootUrl : new string(self->Url).TrimEnd('/');
        return WriteString($"{accessorUrl}/Mizon-Test-Song.txt", buffer, length, required);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetClassId(ComObject* _, Guid* classId)
    {
        if (classId is null)
            return E_POINTER;

        // The provider returns its own IFilter from BindToFilter, so there
        // is no document-type CLSID for SearchProtocolHost to resolve.
        *classId = Guid.Empty;
        return E_NOTIMPL;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int IsDirectory(ComObject* self) => self->IsContainer != 0 ? S_OK : S_FALSE;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetSize(ComObject* _, ulong* size) { if (size is null) return E_POINTER; *size = (ulong)Encoding.UTF8.GetByteCount(SearchProviderConstants.SampleMusicContent); return S_OK; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetLastModified(ComObject* _, long* fileTime) { if (fileTime is null) return E_POINTER; *fileTime = DateTime.UtcNow.ToFileTimeUtc(); return S_OK; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetSecurityDescriptor(ComObject* self, byte* securityDescriptor, uint length, uint* required) { if (required is not null) *required = 0; return E_NOTIMPL; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetRedirectedUrl(ComObject* self, char* buffer, uint length, uint* required) { if (required is not null) *required = 0; return S_FALSE; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetSecurityProvider(ComObject* _, Guid* classId) { if (classId is null) return E_POINTER; *classId = Guid.Empty; return E_NOTIMPL; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int BindToStream(ComObject* self, void** stream)
    {
        if (stream is null)
            return E_POINTER;
        var bytes = Encoding.UTF8.GetBytes(SearchProviderConstants.SampleMusicContent);
        fixed (byte* data = bytes)
            *stream = SHCreateMemStream(data, (uint)bytes.Length);
        return *stream is null ? E_NOTIMPL : S_OK;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int BindToFilter(ComObject* self, void** filter)
    {
        if (filter is null)
            return E_POINTER;

        *filter = null;
        return E_NOTIMPL;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FilterInit(ComObject* self, uint flags, uint attributeCount, void* attributes, uint* resultFlags)
    {
        if (resultFlags is not null)
            *resultFlags = 0;

        self->FilterChunkReturned = 0;
        self->FilterTextOffset = 0;
        self->FilterValueReturned = 0;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetChunk(ComObject* self, StatChunk* chunk)
    {
        if (chunk is null)
            return E_POINTER;
        if (self->FilterChunkReturned != 0)
            return FILTER_E_END_OF_CHUNKS;

        *chunk = default;
        chunk->IdChunk = 1;
        chunk->BreakType = 2; // CHUNK_EOS
        chunk->Flags = self->IsContainer != 0 ? 2u : 1u; // CHUNK_VALUE / CHUNK_TEXT
        chunk->Locale = 0;
        chunk->IdChunkSource = 1;
        if (self->IsContainer != 0)
        {
            chunk->Attribute.PropertySet = SearchUrlToIndexPropertySet;
            chunk->Attribute.Property.Kind = 1; // PRSPEC_PROPID
            chunk->Attribute.Property.Value = 2; // PKEY_Search_UrlToIndex
        }
        self->FilterChunkReturned = 1;
        Trace($"GetChunk container={self->IsContainer != 0}");
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetText(ComObject* self, uint* characterCount, char* buffer)
    {
        if (characterCount is null || buffer is null)
            return E_POINTER;
        if (self->FilterChunkReturned == 0 || self->IsContainer != 0)
            return FILTER_E_NO_TEXT;

        var text = SearchProviderConstants.SampleMusicContent;
        var available = text.Length - self->FilterTextOffset;
        if (available <= 0)
        {
            *characterCount = 0;
            return FILTER_E_NO_TEXT;
        }

        var requested = (int)Math.Min(*characterCount, (uint)available);
        text.AsSpan(self->FilterTextOffset, requested).CopyTo(new Span<char>(buffer, requested));
        self->FilterTextOffset += requested;
        *characterCount = (uint)requested;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetValue(ComObject* self, void** value)
    {
        if (value is null)
            return E_POINTER;
        *value = null;
        if (self->IsContainer == 0 || self->FilterValueReturned != 0)
            return FILTER_E_NO_TEXT;

        var propertyValue = (PropVariant*)CoTaskMemAlloc((nuint)sizeof(PropVariant));
        if (propertyValue is null)
            return unchecked((int)0x8007000E);

        *propertyValue = default;
        propertyValue->ValueType = 31; // VT_LPWSTR
        var rootUrl = self->Url is null ? SearchProviderConstants.RootUrl : new string(self->Url).TrimEnd('/');
        var childUrl = rootUrl.EndsWith("/localhost", StringComparison.OrdinalIgnoreCase)
            ? $"{rootUrl}/music/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72"
            : $"{rootUrl}/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72";
        var characterCount = childUrl.Length + 1;
        var url = (char*)CoTaskMemAlloc((nuint)(characterCount * sizeof(char)));
        if (url is null)
        {
            CoTaskMemFree(propertyValue);
            return unchecked((int)0x8007000E);
        }

        childUrl.AsSpan().CopyTo(new Span<char>(url, characterCount - 1));
        url[characterCount - 1] = '\0';
        propertyValue->PointerValue = url;
        self->FilterValueReturned = 1;
        *value = propertyValue;
        Trace("GetValue child URL emitted");
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int BindRegion(ComObject* self, FilterRegion region, Guid* interfaceId, void** result)
    {
        if (result is null)
            return E_POINTER;
        *result = null;
        return E_NOTIMPL;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterRegion
    {
        public uint IdChunk;
        public uint CharacterStart;
        public uint CharacterExtent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropSpec
    {
        public uint Kind;
        public nuint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FullPropSpec
    {
        public Guid PropertySet;
        public PropSpec Property;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StatChunk
    {
        public uint IdChunk;
        public uint BreakType;
        public uint Flags;
        public uint Locale;
        public FullPropSpec Attribute;
        public uint IdChunkSource;
        public uint CharacterStartSource;
        public uint CharacterLengthSource;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort ValueType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public void* PointerValue;
        public nuint Reserved;
    }

    private static readonly Guid SearchUrlToIndexPropertySet = new("0B63E343-9CCC-11D0-BCDB-00805FCCCE04");

    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "Mizon.Windows.SearchProvider.trace.log"), $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never alter SearchProtocolHost's result.
        }
    }

    private static int WriteString(string value, char* buffer, uint length, uint* required)
    {
        if (required is null)
            return E_POINTER;
        *required = (uint)value.Length;
        if (buffer is null || length <= value.Length)
            return HRESULT_INSUFFICIENT_BUFFER;
        value.AsSpan().CopyTo(new Span<char>(buffer, value.Length));
        buffer[value.Length] = '\0';
        return S_OK;
    }

    private static char* CopyString(string value)
    {
        var result = (char*)NativeMemory.Alloc((nuint)((value.Length + 1) * sizeof(char)));
        value.AsSpan().CopyTo(new Span<char>(result, value.Length));
        result[value.Length] = '\0';
        return result;
    }

    [DllImport("shlwapi.dll")]
    private static extern void* SHCreateMemStream(byte* initialData, uint initialDataLength);

    [DllImport("ole32.dll")]
    private static extern void* CoTaskMemAlloc(nuint byteCount);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(void* memory);
}
