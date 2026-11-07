namespace Meziantou.Framework.Imaging.CorpusGenerator;

internal static partial class FontCorpus
{
    /// <summary>
    /// The languages the reference shaper is asked to shape a text in with the font that has a language system for each
    /// tag of the registry of OpenType (<c>FontSamples.Languages</c>), as BCP 47 language tags: the codes of two letters
    /// of ISO 639-1, the codes of the registry that have no code of two letters, the languages the language subtag
    /// registry of BCP 47 gives a macrolanguage or a replacement for, and tags with a script, a region or a variant
    /// that select a written form.
    /// </summary>
    private const string ShapingLanguages = """
        aa ab ae af ak am an ar as av ay az ba be bg bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz ee el en eo es et eu fa ff
        fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik io is it iu ja jv ka kg ki kj kk kl km kn ko kr ks
        ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml mn mr ms mt my na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps
        pt qu rm rn ro ru rw sa sc sd se sg si sk sl sm sn so sq sr ss st su sv sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz
        ve vi vo wa wo xh yi yo za zh zu aaq abq abs acf ach acr acy ada ady afs agu ahg aht aig aii aio aiw akb alt amf amw ang aoa apa
        apj apk apl apm apw arn ast ath atj ats atv auj avn awa azb azd azn azz bad bah bai bal ban bar bbc bbj bbp bci bcl bcq bcr bdc
        bdy bea beb bem ber bew bfl bfq bft bfu bfy bgc bgq bgr bhb bhi bhk bho bik bin biu bjj bjo bjs bjt bko bla ble blk bln bpd bpl
        bpq bpy bqi bqk bra brc brh brx bsk btb btd btk btm bto bts btx btz bug bum bwe bxo bxp bxr byn byv bzj bzk caa cac caf cak cay
        cbg cbk cbl ccl ccm cco ceb cek cey cfm cgg chf chj chk chn cho chp chq chr chy chz cja cjm cka ckn cks ckt ckz clc cld cle clj
        clt cmi cmr cnb cnh cnk cnl cnr cnt cnu cnw cob coo cop coq cpa cpe cpf cpi cpp crh cri crj crk crl crm crp crs crx csa csb csh
        csj cso csv csw csy ctc ctd cte ctg cth ctl cto cts ctt ctu cuc cuk cvn cwd czt dag dao dap dar dax dcr den dep dgo dgr dhd dhg
        din diq dje djk djr dng dnj doi dsb duj dwu dwy dyu efi eky emk emp emy enf enh esu eto eve evn ewo fab fan fat fbl fil fmp fng
        fon fpe frc frp fuf fur fuv gaa gac gag gbm gce gcf gcl gcr gez gha ghc ghk gho gib gih gil gkp gld glk gnb gnn gog gon goq gox
        gpe grr grt gru gsw guc guf guk gul guz gwi gyn haa hai har haw hax hay haz hca hdn hei hil hlt hmd hmn hmr hmz hnd hne hno hoc
        hoi hoj hra hsb hup hur hus hwc hyw iba ibb iby icr idb igb ihb ijc ije ijn ijo ijs ike ilo ing inh iru itz ixl jac jam jbe jbn
        jbo jct jdt jgo jkm jkp jvd kaa kab kam kar kaw kbc kbd kca kcn kde kdr kdt kea kek kex kfa kfr kfx kfy kgf kha khb kht khw kiu
        kjb kjd kjh kjj kjp kjt kjz kkz kln kmb kmg kmv kmw kmz knj koi kok kos koy kpe kpv kpy kqs kqy krc kri krl kru ksh kss ksu ksw
        ktb ktu ktw kum kuu kuw kvl kvq kvt kvu kvy kwk kww kxc kxf kxk kxu kyu lac lad lbe lbj lbl lef lez lif lij lir lis liv liy ljp
        lki lld lmn lmo lna lnl lom lou lpo lrc lrt lua luo lus lut luy luz lzz mad mag mai mak mam man max mbf mbo mcm mct mdf mdr mdy
        men mer mev mfa mfe mfp mga mhc mhr mhv min mkn mku mkw mlq mnc mnh mni mnk mns mnw mod moh mol mop mos mpe mrh mrj msc msi mtr
        mud mus mvb mve mwk mwl mwq mwr mww mym myn myq myv mzb mzn mzs nag nah nap naz nch nci ncj ncl ncx ndc nds nef new nga ngl ngm
        ngo ngu nhc nhe nhg nhi nhk nhm nhn nhp nhq nht nhv nhw nhx nhy nhz niu niv njt njz nkx nla nlv nnh nnz nod noe nog nop nov npl
        nqo nsk nso nsu nue nuk nuu nuz nwe nym nyn nza ojs okd okm okr one ono onx oor orr oua pag pam pap pau pcc pcd pce pck pcm pdc
        pdu pea pey pga phk pih pis pkh plg pll pln plp pml pms pmy pnb poc poh pon pov pre pro prs pub pwo pww quc quh qum quv quz qvi
        qwh qwt raj ral rar rbb rbl rcf rej rhg ria rif rit rki rkw rmy rmz rom rop rtc rtm rue rup rwr sah sam sas sat scf sch sci sck
        scn sco scs sds see seh sek sel sez sfm sga sgs sgw shi shl shn shy sid siz sja sjd sje sjo sjs sju skr skw sma smj smn sms smt
        snk sop srm srn srr srs sta stq str stv suk suq sva svc swb swn swv sxu syc syl syr szl taa tab taq tas tau tbv tcb tce tch tcp
        tcs tcy tcz tdc tdd tem tet tez tfn tgh tgj tgx thp tht thv thz tia tig tiv tjl tjo tli tly tmg tmh tod toi toj tol tor tpi trf
        tru tsj ttc ttm ttq tum tus tuu tvl tvy txc tyv tyz tzh tzj tzm tzo ubl udi udm uln umb unr usp vap vec vic vkp vls vmw vro war
        wbl wbm wbr wci wdt wea wes weu wlc wle wlk wni wry wtm wyn xal xan xjb xkf xmg xmm xnj xnq xog xpe xsl xst xub xuj xup yaj yao
        yap ybb ycr ygp yna yua yuf ywq zdj zea zen zgh zmz zne zom zum zyp zza aae aam aao aat abh abv acm acq acw acx adf adp aeb aec
        afb ajp ajt aju aln als apc apd arb arq ars ary arz asd aue auz avl ayc ayh ayl ayn ayp ayr ayx azj bbz bcc bdt bgm bgn bgp bh
        bhr bic bjd bjn bjq blg bmm btj bve bvu bxk bxm bxu bzc ccq cdo ciw cjr cjy ckb cls cmk cmn cnp coa coy cpx cqd cqu csp czh czo
        dek dib dik dip dit diw dks drh drr drw dty dup ebk ekk enb esg esi esk eyo ffm fub fuc fue fuh fui fuq gan gav gax gaz gbo gbp
        gbq gda gec gfx ggn gju gli gmm gno gnw gom grj grv gry gso gti gug gui gun guv gya hae hak hea hji hma hmc hme hmg hmh hmi hmj
        hml hmm hmp hmq hms hmw hmy hnj hnm hrm hrr hsn huj ibi ida ikt ilw in iw jak jat jax jeg ji jw jye kby kgc kgh kgm khk kmr knc
        kng knn koj krm krt ktr kvb kvr kvs kwq kwy kxd kxe kxl kzj kzt lak lbk lce lcf ldi lii liw lkb lko lks llo lmm lri lrm lsm ltg
        lto lts luh lvs lwg lzh meg meo mfb mmr mnp mo mqg mrd msh mst mui mup muq mvf mwj myd myt nad nan ncp nhd niq nle nns nnx nom
        npi nte nts nxu nyd obk ojb ojc ojg ojw oki orc orn ors ory otw oun pat pbt pbu pcr pel pes phr pko plt pmc pmk pmu ppa ppr prp
        pry pse pst puz qub qud quf qug quk qul qup qur qus quw qux quy qva qvc qve qvh qvj qvl qvm qvn qvo qvp qvs qvw qvz qwa qwc qws
        qxa qxc qxh qxl qxn qxo qxp qxr qxt qxu qxw rag rbk rmc rmf rml rmn rmo rmw sca sdc sdh sdn sgc shu sjc skg skk smd snb spv spy
        src sro ssh swc swh szd tdu tdx tec thc thw thx tie tkg tkk tlw tmk tmp tmw tne tnf tpw tsf tuy txy umu unm uok urk uzn uzs vbk
        vkk vkt vsn wsg wuu xba xhe xia xkh xmv xmw xnr xrq xss ybd ydd yhd yih yma ymt yol yos yud yue yuu zaa zab zac zad zae zaf zai
        zam zao zaq zar zas zat zav zaw zax zca zcd zch zeh zgb zgm zgn zhd zhn zir zkb zlj zlm zln zlq zmi zoo zpa zpb zpc zpd zpe zpf
        zpg zph zpi zpj zpk zpl zpm zpn zpo zpp zpq zpr zps zpt zpu zpv zpw zpx zpy zpz zqe zsm zsr zte ztg ztl ztm ztn ztp ztq zts ztt
        ztu ztx zty zyb zyg zyj zyn zzj sr-Cyrl-RS fr-CA de-CH-1901 en-US zh-Hans zh-Hant zh-CN zh-SG zh-TW zh-HK zh-MO zh-Hant-HK
        zh-Hant-MO zh-Hans-HK zh-Hans-TW zh-Latn zh-Bopo zh-yue zh-cmn zh-cmn-Hant cmn-Hant cmn-TW yue-Hant yue-Hans yue-HK yue-CN
        lzh-Hans el-polyton syr-Syre syr-Syrj syr-Syrn syc-Syre ka-Geok und-fonipa en-fonipa und-fonnapa fi-fonupa ro-MD und x-private
        en-x-zh mul zxx
        """;

    /// <summary>The letter A, in each language of <see cref="ShapingLanguages"/> and without language.</summary>
    private static ShapingText[] CreateLanguageShapingCases()
    {
        var cases = new List<ShapingText> { new("A", Features: null) };
        foreach (var language in ShapingLanguages.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            cases.Add(new ShapingText("A", Features: null, Language: language));
        }

        return [.. cases];
    }
}
