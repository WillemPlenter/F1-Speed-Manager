using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal sealed class PeImage
{
    internal readonly byte[] Bytes;
    internal readonly int Pe, Optional;
    internal readonly uint Timestamp, ImageSize;
    internal readonly List<Section> Sections = new List<Section>();
    internal sealed class Section { internal uint Rva, VirtualSize, Raw, RawSize, Flags; }
    internal PeImage(byte[] bytes)
    {
        Bytes = bytes;
        Require(bytes.Length > 0x100 && U16(0) == 0x5a4d, "Not a PE executable");
        Pe = checked((int)U32(0x3c));
        Require(U32(Pe) == 0x4550 && U16(Pe+4) == 0x8664, "Expected AMD64 PE");
        Optional = Pe+24;
        Require(U16(Optional)==0x20b, "Expected PE32+");
        Timestamp=U32(Pe+8); ImageSize=U32(Optional+56);
        int section=Optional+U16(Pe+20), count=U16(Pe+6);
        Require(count > 0 && count <= 96, "Invalid section count");
        for(int i=0;i<count;i++,section+=40)
            Sections.Add(new Section {VirtualSize=U32(section+8),Rva=U32(section+12),RawSize=U32(section+16),Raw=U32(section+20),Flags=U32(section+36)});
    }
    internal static void Require(bool ok,string why) { if(!ok) throw new InvalidDataException(why); }
    internal ushort U16(int p) { Bounds(p,2); return BitConverter.ToUInt16(Bytes,p); }
    internal uint U32(int p) { Bounds(p,4); return BitConverter.ToUInt32(Bytes,p); }
    internal ulong U64(int p) { Bounds(p,8); return BitConverter.ToUInt64(Bytes,p); }
    void Bounds(int p,int n) { Require(p>=0 && n>=0 && p<=Bytes.Length-n,"PE bounds check failed"); }
    internal int Offset(uint rva,int length)
    {
        foreach(var s in Sections) if(rva>=s.Rva && (ulong)rva+(uint)length<=(ulong)s.Rva+s.RawSize)
        { int result=checked((int)(s.Raw+rva-s.Rva)); Bounds(result,length); return result; }
        throw new InvalidDataException("RVA outside file-backed section");
    }
    string Ascii(uint rva) { int p=Offset(rva,1),end=p; while(end<Bytes.Length && end-p<256 && Bytes[end]!=0)end++; Require(end-p<256 && end<Bytes.Length,"Invalid PE string"); return Encoding.ASCII.GetString(Bytes,p,end-p); }
    internal uint FindQpcImport()
    {
        uint directory=U32(Optional+120), size=U32(Optional+124), found=0;
        Require(directory!=0 && size>=20 && size<65536,"Invalid import directory");
        for(uint d=0;d+20<=size;d+=20)
        {
            int p=Offset(directory+d,20); uint names=U32(p),dll=U32(p+12),iat=U32(p+16);
            if(names==0 && dll==0 && iat==0) break;
            Require(names!=0 && dll!=0 && iat!=0,"Missing named import table");
            if(!Ascii(dll).Equals("KERNEL32.dll",StringComparison.OrdinalIgnoreCase))continue;
            for(uint n=0;n<16384;n++)
            {
                ulong entry=U64(Offset(checked(names+n*8),8)); if(entry==0)break;
                if((entry&0x8000000000000000UL)!=0)continue;
                Require(entry<=uint.MaxValue,"Invalid import name RVA");
                if(Ascii(checked((uint)entry+2))!="QueryPerformanceCounter")continue;
                Require(found==0,"Ambiguous QPC import"); found=checked(iat+n*8);
            }
        }
        Require(found!=0 && found%8==0 && found<ImageSize-8,"No unique aligned QPC import");
        return found;
    }
    internal uint FindConversion(uint iat)
    {
        // Direct QPC call followed by int64-to-double conversion and seconds scaling.
        foreach(var s in Sections)
        {
            if((s.Flags&0x20000000)==0) continue;
            int end=checked((int)Math.Min((long)Bytes.Length,(long)s.Raw+s.RawSize))-32;
            for(int p=checked((int)s.Raw);p<end;p++)
            {
                if(Bytes[p]!=0xff || Bytes[p+1]!=0x15)continue;
                long dest=(long)s.Rva+p-s.Raw+6+BitConverter.ToInt32(Bytes,p+2);
                if(dest!=iat)continue;
                if(Bytes[p+6]==0x0f && Bytes[p+7]==0x57 && Bytes[p+8]==0xc0 && Bytes[p+9]==0xf2 && Bytes[p+10]==0x48 && Bytes[p+11]==0x0f && Bytes[p+12]==0x2a && Bytes[p+16]==0xf2 && Bytes[p+17]==0x0f && Bytes[p+18]==0x59)
                    return checked(s.Rva+(uint)p-s.Raw);
            }
        }
        throw new InvalidDataException("Expected QPC-to-seconds code was not found");
    }
}
