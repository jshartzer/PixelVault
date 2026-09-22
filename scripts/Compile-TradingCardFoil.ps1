# Rebuild the checked-in WPF shader after editing TradingCardFoil.hlsl.
# Uses Windows' compiler; no SDK installation or runtime compilation is needed.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class FoilShaderCompiler {
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBlob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] IntPtr GetBufferSize(); }
    [DllImport("d3dcompiler_47.dll", CharSet=CharSet.Ansi)]
    static extern int D3DCompile(byte[] source, UIntPtr size, string name, IntPtr defines, IntPtr include,
        string entry, string target, uint flags, uint flags2, out IBlob code, out IBlob errors);
    public static void Compile(string input, string output) {
        var bytes = File.ReadAllBytes(input); IBlob code, errors;
        int hr = D3DCompile(bytes, (UIntPtr)bytes.Length, input, IntPtr.Zero, IntPtr.Zero, "main", "ps_3_0", 1u << 15, 0, out code, out errors);
        try {
            if (hr < 0) throw new Exception(errors == null ? "Shader compilation failed" : Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
            var compiled = new byte[code.GetBufferSize().ToInt32()];
            Marshal.Copy(code.GetBufferPointer(), compiled, 0, compiled.Length); File.WriteAllBytes(output, compiled);
        } finally { if (code != null) Marshal.ReleaseComObject(code); if (errors != null) Marshal.ReleaseComObject(errors); }
    }
}
'@
$foilDirectory = Join-Path $PSScriptRoot '../src/PixelVault.Native/UI/Effects'
[FoilShaderCompiler]::Compile((Join-Path $foilDirectory 'TradingCardFoil.hlsl'), (Join-Path $foilDirectory 'TradingCardFoil.ps'))
