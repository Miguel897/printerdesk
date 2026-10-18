using System.Runtime.InteropServices;

namespace PrinterDesk.Core.Pdfium;

/// <summary>Minimal P/Invoke surface of pdfium.dll (bblanchon.PDFium.Win32). Call only from <see cref="PdfiumThread"/>.</summary>
internal static partial class PdfiumNative
{
    private const string Lib = "pdfium";

    public const int BitmapBgra = 4;

    public const int RenderAnnotations = 0x01;
    public const int RenderPrinting = 0x800;

    public const uint ErrPassword = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct SizeF
    {
        public float Width;
        public float Height;
    }

    [LibraryImport(Lib)]
    public static partial void FPDF_InitLibrary();

    [LibraryImport(Lib)]
    public static partial nint FPDF_LoadMemDocument64(nint data, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

    [LibraryImport(Lib)]
    public static partial uint FPDF_GetLastError();

    [LibraryImport(Lib)]
    public static partial void FPDF_CloseDocument(nint document);

    [LibraryImport(Lib)]
    public static partial int FPDF_GetPageCount(nint document);

    [LibraryImport(Lib)]
    public static partial int FPDF_GetPageSizeByIndexF(nint document, int pageIndex, out SizeF size);

    [LibraryImport(Lib)]
    public static partial nint FPDF_LoadPage(nint document, int pageIndex);

    [LibraryImport(Lib)]
    public static partial void FPDF_ClosePage(nint page);

    [LibraryImport(Lib)]
    public static partial nint FPDFBitmap_CreateEx(int width, int height, int format, nint firstScan, int stride);

    [LibraryImport(Lib)]
    public static partial int FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);

    [LibraryImport(Lib)]
    public static partial void FPDFBitmap_Destroy(nint bitmap);

    [LibraryImport(Lib)]
    public static partial void FPDF_RenderPageBitmap(nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [LibraryImport(Lib)]
    public static partial nint FPDFDOC_InitFormFillEnvironment(nint document, nint formInfo);

    [LibraryImport(Lib)]
    public static partial void FPDFDOC_ExitFormFillEnvironment(nint form);

    [LibraryImport(Lib)]
    public static partial void FORM_OnAfterLoadPage(nint page, nint form);

    [LibraryImport(Lib)]
    public static partial void FORM_OnBeforeClosePage(nint page, nint form);

    [LibraryImport(Lib)]
    public static partial void FPDF_FFLDraw(nint form, nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
}
