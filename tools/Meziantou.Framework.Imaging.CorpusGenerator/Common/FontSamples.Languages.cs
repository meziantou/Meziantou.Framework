namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

internal static partial class FontSamples
{
    /// <summary>
    /// The registry of language system tags of the OpenType specification (OpenType 1.9.1, page of 2024-12-05), as its
    /// rows give it: each tag (followed by <c>!</c> when it is deprecated) with the ISO 639 codes of its languages.
    /// </summary>
    /// <remarks>
    /// It is transcribed from the page by a script, not from the table of the library, so that the tests compare the
    /// table of the library with a second reading of the registry.
    /// </remarks>
    public const string LanguageSystemRegistry = """
        AAQ:aaq ABA:abq ABK:abk ACH:ach ACR:acr ACY:acy ADY:ady AFK:afr AFR:aar AGW:ahg AIO:aio AKA:aka,fat,twi AKB:akb ALS:gsw
        ALT:atv,alt AMH:amh ANG:ang APPH ARA:ara ARG:arg ARI:aiw ARK:mhv,rmz,rki ASM:asm AST:ast
        ATH:aht,apa,apk,apj,apl,apm,apw,ath,bea,sek,bcr,caf,chp,clc,coq,crx,ctc,den,dgr,gce,gwi,haa,hoi,hup,ing,kkz,koy,ktw,kuu,mvb,nav,qwt,scs,srs,taa,tau,tcb,tce,tfn,tgx,tht,tol,ttm,tuu,txc,wlk,xup,xsl
        ATS:ats AVN:avn AVR:ava AWA:awa AYM:aym AZB:azb AZE:aze BAD:bfq
        BAD0:bad,bbp,bfl,bjo,bpd,bqk,gox,kuw,liy,lna,lnl,mnh,nue,nuu,tor,yaj,zmz BAG:bfy BAL:krc BAN:ban BAR:bar BAU:bci BBC:bbc
        BBR:auj,ber,cnu,gha,gho,grr,jbe,jbn,kab,mzb,oua,rif,sds,shi,shy,siz,sjs,swn,taq,tez,thv,thz,tia,tjo,tmh,ttq,tzm,zen,zgh BCH:bcq
        BCR BDC:bdc BDY:bdy BEL:bel BEM:bem BEN:ben BGC:bgc BGQ:bgq BGR:bul BHI:bhi,bhb BHO:bho
        BIK:bik,bhk,bcl,bto,cts,bln,fbl,lbl,rbl,ubl BIL:byn BIS:bis BJJ:bjj BKF:bla BLI:bal BLK:blk BLN:bjt,ble BLT:bft BMB:bam
        BML:bai,bbj,bko,byv,fmp,jgo,nla,nnh,nnz,nwe,xmg,ybb BOS:bos BPY:bpy BRE:bre BRH:brh BRI:bra BRM:mya BRX:brx BSH:bak BSK:bsk
        BTD:btd BTI:btb,beb,bum,bxp,eto,ewo,mct BTK:akb,bbc,btd,btk,btm,bts,btx,btz BTM:btm BTS:bts BTX:btx BTZ:btz BUG:bug BYV:byv
        CAK:cak CAT:cat CAY:cay CBG:cbg CBK:cbk CCHN:cco,chj,chq,chz,cle,cnl,cnt,cpa,csa,cso,cte,ctl,cuc,cvn CEB:ceb CGG:cgg CHA:cha
        CHE:che CHG:sgw CHH:hne CHI:nya CHK:ckt CHK0:chk CHO:cho CHP:chp CHR:chr CHU:chv CHY:chy CJA:cja CJM:cjm CMI:cmi
        CMR:swb,wlc,wni,zdj COO:coo COP:cop COR:cor COS:cos
        CPP:abs,acf,afs,aig,aoa,bah,bew,bis,bjs,bpl,bpq,brc,bxo,bzj,bzk,cbk,ccl,ccm,chn,cks,cpe,cpf,cpi,cpp,cri,crp,crs,dcr,dep,djk,fab,fng,fpe,gac,gcf,gcl,gcr,gib,goq,gpe,gul,gyn,hat,hca,hmo,hwc,icr,idb,ihb,jam,jvd,kcn,kea,kmv,kri,kww,lir,lou,lrt,max,mbf,mcm,mfe,mfp,mkn,mod,msi,mud,mzs,nag,nef,ngm,njt,onx,oor,pap,pcm,pea,pey,pga,pih,pis,pln,pml,pmy,pov,pre,rcf,rop,scf,sci,skw,srm,srn,sta,svc,tas,tch,tcs,tgh,tmg,tpi,trf,tvy,uln,vic,vkp,wes,xmm,ycr
        CRE:cre CRR:crx,caf CRT:crh CSB:csb CSL:chu CSY:ces CTG:ctg CTO:cto CTT:ctt CUK:cuk DAG:dag DAN:dan DAR:dar DAX:dax DCR:cwd
        DEU:deu DGO:dgo DGR:doi DHG:dhg DHV!:div DIQ:diq DIV:div DJR:dje DJR0:djr DNG:ada DNJ:dnj DNK:din DRI:prs DUJ:duj,dwu,dwy DUN:dng
        DZN:dzo EBI:igb ECR:crj,crl EDO:bin EFI:efi ELL:ell EMK:emk EMP:emp ENG:eng ERZ:myv ESP:spa ESU:esu ETI:est EUQ:eus EVK:evn
        EVN:eve EWE:ewe FAN:acf FAN0:fan FAR:fas FAT:fat FIN:fin FJI:fij FLE:vls FMP:fmp FNE:enf FON:fon FOS:fao FRA:fra FRC:frc FRI:fry
        FRL:fur FRP:frp FTA:fuf FUL:ful FUV:fuv GAD:gaa GAE:gla GAG:gag GAL:glg GAR GAW:gbm GEZ:gez GIH:gih GIL:niv GIL0:gil GKP:gkp
        GLK:glk GMZ:guk GNN:gnn GOG:gog GON:gon GRN:kal GRO:grt GUA:grn GUC:guc GUF:guf GUJ:guj GUZ:guz HAI:hat HAI0:hai,hax,hdn HAL:cfm
        HAR:hoj HAU:hau HAW:haw HAY:hay HAZ:haz HBN:amf HEI:hei HER:her HIL:hil HIN:hin HMA:mrj HMD:hmd HMN:hmn HMO:hmo HMZ:hmz
        HND:hno,hnd HO:hoc HRI:har HRV:hrv HUN:hun HUR:hur HYE:hye,hyw HYE0:hye IBA:iba IBB:ibb IBO:ibo IDO:ido
        IJO:iby,ijc,ije,ijn,ijo,ijs,nkx,okd,okr,orr ILE:ile ILO:ilo INA:ina IND:ind ING:inh INU:iku INUK:ike,iku IPK:ipk IPPH IRI:gle
        IRT:ghc,gle IRU:iru ISL:isl ISM:smn ITA:ita IWR:heb JAM:jam JAN:jpn JAV:jav JBO:jbo JCT:jct JDT:jdt JII:yid JUD:lad JUL:dyu
        KAB:kbd KAB0:kab KAC:kfr KAL:kln KAN:kan KAR:krc KAT:kat KAW:kaw KAZ:kaz KBC:kbc KDE:kde KEA:kea KEB:ktb KEK:kek KGE:kat KGF:kgf
        KHA:kjh KHK:kca KHM:khm KHS:kca KHT:kht KHV:kca KHW:khw KIK:kik KIR:kir KIS:kqs,kss KIU:kiu KJD:kjd KJJ:kjj KJP:kjp KJZ:kjz
        KKN:kex KLM:xal KMB:kam KMG:kmg KMN:kfy KMO:kmw KMS:kxc KMZ:kmz KNR:kau KOD:kfa KOH:kor,okm KOK:kok KOM:kom KON:ktu KON0:kon
        KOP:koi KOR:kor KOS:kos KOZ:kpv KPL:kpe KRI:kri KRK:kaa KRL:krl KRM:kdr
        KRN:blk,bwe,eky,ghk,jkm,jkp,kar,kjp,kjt,ksw,kvl,kvq,kvt,kvu,kvy,kxf,kxk,kyu,pdu,pwo,pww,wea KRT:kqy KSH:kas KSH0:ksh KSI:kha
        KSM:sjd KSU:ksu KSW:ksw KUA:kua KUI:kxu KUL:kfx KUM:kum KUR:kur KUU:kru KUY:kdt KVQ:kvq KWK:kwk KYK:kpy KYU:kyu LAD:lld LAH:bfu
        LAK:lbe LAM:lmn LAO:lao LAT:lat LAZ:lzz LCR:crm LDK:lbj LEF:lef LEZ:lez LIJ:lij LIM:lim LIN:lin LIS:lis LIV:liv LJP:ljp LKI:lki
        LMA:mhr LMB:lif LMO:lmo LMW:ngl LOM:lom LPO:lpo LRC:lrc,luz,bqi,zum LSB:dsb LSM:smj LTH:lit LTZ:ltz LUA:lua LUB:lub LUG:lug
        LUH:luy LUO:luo LUT:lut LVI:lav MAD:mad MAG:mag MAH:mah MAJ:mpe MAK:vmw MAL:mal MAM:mam MAN:mns MAP:arn MAR:mar
        MAW:mwr,dhd,rwr,mve,wry,mtr,swv MBN:kmb MBO:mbo MCH:mnc MCR:crm MDE:men MDR:mdr MEN:mym MER:mer MEV:mev MFA:mfa MFE:mfe MIN:min
        MIZ:lus MKD:mkd MKR:mak MKW:mkw MLE:mdy MLG:mlg MLN:mlq MLR:mal MLY:msa MND:mnk MNG:mon MNI:mni
        MNK:man,mnk,myq,mku,msc,emk,mwk,mlq MNX:glv MOH:moh MOK:mdf MOL:mol MON:mnw MONT:mnw MOR MOS:mos MRI:mri MTH:mai MTS:mlt MUN:unr
        MUS:mus MWL:mwl MWW:mww
        MYN:acr,agu,caa,cac,cak,chf,ckz,cob,ctu,emy,hus,itz,ixl,jac,kek,kjb,knj,lac,mam,mhc,mop,myn,poc,poh,quc,qum,quv,toj,ttc,tzh,tzj,tzo,usp,yua
        MZN:mzn NAG:nag
        NAH:azd,azn,azz,nah,naz,nch,nci,ncj,ncl,ncx,ngu,nhc,nhe,nhg,nhi,nhk,nhm,nhn,nhp,nhq,nht,nhv,nhw,nhx,nhy,nhz,nlv,npl,nsu,nuz
        NAN:gld NAP:nap NAS:nsk NAU:nau NAV:nav NCR:csw NDB:nbl,nde NDC:ndc NDG:ndo NDS:nds NEP:nep NEW:new NGA:nga NGR NHC:csw
        NIS:dap,njz,tgj NIU:niu NKL:nyn NKO:nqo NLD:nld NOE:noe NOG:nog NOP:nop NOR:nob NOV:nov NSM:sme NSO:nso NTA:nod NTO:epo NUK:nuk
        NYM:nym NYN:nno NZA:nza OCI:oci OCR:ojs OJB:oji ONE:one ONO:ono ORI:ori ORO:orm OSS:oss PAA:sam PAG:pag PAL:pli PAM:pam PAN:pan
        PAP:plp PAP0:pap PAS:pus PAU:pau PCC:pcc PCD:pcd PDC:pdc PGR:ell PHK:phk PIH:pih PIL:fil PLG:pce,rbb,pll PLG0:plg PLK:pol PMS:pms
        PNB:pnb POH:poh PON:pon PRO:pro PTG:por PWO:pwo
        QIN:bgr,biu,cek,cey,cfm,cbl,cka,ckn,clj,clt,cmr,cnb,cnh,cnk,cnw,csh,csj,csv,csy,ctd,cth,czt,dao,gnb,hlt,hmr,hra,lus,mrh,mwq,pck,pkh,pub,ral,rtc,sch,sez,shl,smt,tcp,tcz,vap,weu,zom,zyp
        QUC:quc QUH:quh QUZ:quz QVI:qvi QWH:qwh RAJ:raj RAR:rar RBU:bxr RCR:atj REJ:rej RIA:ria RHG:rhg RIF:rif RIT:rit RKW:rkw RMS:roh
        RMY:rmy ROM:ron ROY:rom RSY:rue RTM:rtm RUA:kin RUN:run RUP:rup RUS:rus SAD:sck SAN:san SAS:sas SAT:sat SAY:chp SCN:scn SCO:sco
        SCS:scs SEE:see SEK:xan SEL:sel SFM:sfm SGA:mga,sga SGO:sag SGS:sgs SHI:shi SHN:shn SIB:sjo SID:sid SIG:xst,stv,wle SJA:sja
        SJE:sje SJU:sju SKS:sms SKY:slk SLA:den,scs,xsl SLV:slv SML:som SMO:smo SNA:seh SNA0:sna SND:snd SNH:sin SNK:snk SOG:gru SOP:sop
        SOT:sot SQI:sqi SRB:cnr,srp SRD:srd SRK:skr SRR:srr SSL:xsl SSM:sma STR:str STQ:stq SUK:suk SUN:sun SUR:suq SVA:sva SVE:swe
        SWA:aii SWK:swa SWZ:ssw SXT:ngo,xnj,xnq SXU:sxu SYL:syl SYR:aii,amw,cld,syc,syr,tru SYRE:syc,syr SYRJ:syc,syr SYRN:syc,syr
        SZL:szl TAB:tab TAJ:tgk TAM:tam TAQ:taq TAT:tat TBV:tbv TCR:cwd TDC:tdc TDD:tdd TEL:tel TET:tet TGL:tgl TGN:ton TGR:tig TGY:tir
        THA:tha THT:tah THP:thp THV:thv THZ:thz TIB:bod TIV:tiv TJL:tjl TKM:tuk TLI:tli TMH:taq,thv,thz,tmh,ttq TLY:tly TMN:tem TNA:tsn
        TNE:enh TNG:toi TOD:xal TOD0:tod TPI:tpi TRK:tur TSG:tso TSJ:tsj TTQ:ttq TUA:tru TUL:tcy TUM:tum TUS:tus TUV:tyv TVL:tvl TWI:twi
        TYZ:tyz TZM:tzm TZO:tzo UDI:udi UDM:udm UKR:ukr UMB:umb UPPH URD:urd USB:hsb UYG:uig UZB:uzb VEC:vec VEN:ven VIT:vie VOL:vol
        VRO:vro WA:wbm WAG:wbr WAR:war WBL:wbl WCI:wci WCR:crk WDT:wdt WEL:cym WLF:wol WLN:wln WTM:wtm WYN:wyn XBD:khb XHS:xho XJB:xjb
        XKF:xkf XOG:xog XPE:xpe XUB:xub XUJ:xuj YAK:sah YAO:yao YAP:yap YBA:yor YCR:crj,crk,crl YGP:ygp YIC YIM:iii YNA:yna YUF:yuf
        YWQ:ywq ZEA:zea ZGH:zgh ZHA:zha ZHH:zho ZHP:zho ZHS:zho ZHT:zho ZHTM:zho ZND:zne ZUL:zul ZZA:zza
        """;

    /// <summary>Gets the tags of <see cref="LanguageSystemRegistry"/>, in the order of their bytes.</summary>
    public static string[] LanguageSystemTags()
    {
        var tags = new List<string>();
        foreach (var item in LanguageSystemRegistry.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            tags.Add(item.Split(':')[0].TrimEnd('!').PadRight(4));
        }

        tags.Sort(StringComparer.Ordinal);
        return [.. tags];
    }

    /// <summary>
    /// The boxes font, without its kerning, with a language system for each tag of the registry of OpenType, or for
    /// each of the given tags, under the script <c>latn</c>: a text shaped with it tells which language system was used.
    /// </summary>
    /// <param name="tags">The tags of the language systems, of four characters each and in the order of their bytes; the tags of <see cref="LanguageSystemTags"/> by default.</param>
    /// <remarks>
    /// The language system of the tag of index <c>i</c> has one feature, <c>locl</c>, which replaces A by four glyphs:
    /// the digits of <c>i</c> in base 6, the most significant first, where the digits 0 to 5 are the glyphs of A, B,
    /// C, D, E and F. The default language system has no feature, and leaves A as it is.
    /// </remarks>
    public static FontBuilder Languages(string[]? tags = null)
    {
        tags ??= LanguageSystemTags();
        var count = tags.Length;
        var table = new List<byte>();
        void UInt16(int value)
        {
            table.Add(checked((byte)(value >> 8)));
            table.Add((byte)value);
        }

        // The header.
        const int ScriptList = 10;
        var scriptTable = 4 + (count * 6);
        var featureList = ScriptList + 8 + scriptTable + 6 + (count * 8);
        var lookupList = featureList + 2 + (count * 12);
        UInt16(1);
        UInt16(0);
        UInt16(ScriptList);
        UInt16(featureList);
        UInt16(lookupList);

        // The script list: one script, its default language system without feature, and a language system per tag.
        UInt16(1);
        table.AddRange("latn"u8.ToArray());
        UInt16(8);
        UInt16(scriptTable);
        UInt16(count);
        for (var i = 0; i < count; i++)
        {
            table.AddRange(System.Text.Encoding.ASCII.GetBytes(tags[i]));
            UInt16(scriptTable + 6 + (i * 8));
        }

        UInt16(0);
        UInt16(0xFFFF);
        UInt16(0);
        for (var i = 0; i < count; i++)
        {
            UInt16(0);
            UInt16(0xFFFF);
            UInt16(1);
            UInt16(i);
        }

        // The feature list: a feature per language system, each with its lookup.
        UInt16(count);
        for (var i = 0; i < count; i++)
        {
            table.AddRange("locl"u8.ToArray());
            UInt16(2 + (count * 6) + (i * 6));
        }

        for (var i = 0; i < count; i++)
        {
            UInt16(0);
            UInt16(1);
            UInt16(i);
        }

        // The lookup list: multiple substitutions of A by the four digits of the index of the lookup.
        const int LookupSize = 32;
        UInt16(count);
        for (var i = 0; i < count; i++)
        {
            UInt16(2 + (count * 2) + (i * LookupSize));
        }

        for (var i = 0; i < count; i++)
        {
            UInt16(2);
            UInt16(0);
            UInt16(1);
            UInt16(8);

            UInt16(1);
            UInt16(18);
            UInt16(1);
            UInt16(8);

            UInt16(4);
            UInt16(BoxA + (i / 216));
            UInt16(BoxA + (i / 36 % 6));
            UInt16(BoxA + (i / 6 % 6));
            UInt16(BoxA + (i % 6));

            UInt16(1);
            UInt16(1);
            UInt16(BoxA);
        }

        var font = Boxes();
        font.FamilyName = "Meziantou Languages";
        font.PostScriptName = "MeziantouLanguages-Regular";
        font.KerningPairs.Clear();
        font.RawTables["GSUB"] = [.. table];
        return font;
    }
}
