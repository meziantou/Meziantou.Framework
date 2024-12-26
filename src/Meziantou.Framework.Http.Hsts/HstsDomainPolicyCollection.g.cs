// <auto-generated />
// Data source: https://raw.githubusercontent.com/chromium/chromium/ba720cd91299fe45b6345be1971ee628af9bc3f5/net/http/transport_security_state_static.json 
// Commit date: 2024-12-18T23:38:47.0000000Z
// #nullable enable // Roslyn doesn't like it :(

using System.Collections.Concurrent;

namespace Meziantou.Framework.Http;

partial class HstsDomainPolicyCollection
{
    private void Initialize(TimeProvider timeProvider)
    {
        var expires126Days = timeProvider.GetUtcNow().Add(TimeSpan.FromDays(126));
        var expires365Days = timeProvider.GetUtcNow().Add(TimeSpan.FromDays(365));
        var dict1 = new ConcurrentDictionary<string, HstsDomainPolicy>(concurrencyLevel: -1, capacity: 61, comparer: StringComparer.OrdinalIgnoreCase);
        _policies.Add(dict1);
        var dict2 = new ConcurrentDictionary<string, HstsDomainPolicy>(concurrencyLevel: -1, capacity: 149553, comparer: StringComparer.OrdinalIgnoreCase);
        _policies.Add(dict2);
        var dict3 = new ConcurrentDictionary<string, HstsDomainPolicy>(concurrencyLevel: -1, capacity: 11247, comparer: StringComparer.OrdinalIgnoreCase);
        _policies.Add(dict3);
        var dict4 = new ConcurrentDictionary<string, HstsDomainPolicy>(concurrencyLevel: -1, capacity: 197, comparer: StringComparer.OrdinalIgnoreCase);
        _policies.Add(dict4);
        var dict5 = new ConcurrentDictionary<string, HstsDomainPolicy>(concurrencyLevel: -1, capacity: 11, comparer: StringComparer.OrdinalIgnoreCase);
        _policies.Add(dict5);
        // Segment size: 1
        _ = dict1.TryAdd("amazon", new HstsDomainPolicy("amazon", expires365Days, true));
        _ = dict1.TryAdd("android", new HstsDomainPolicy("android", expires365Days, true));
        _ = dict1.TryAdd("app", new HstsDomainPolicy("app", expires365Days, true));
        _ = dict1.TryAdd("audible", new HstsDomainPolicy("audible", expires365Days, true));
        _ = dict1.TryAdd("azure", new HstsDomainPolicy("azure", expires365Days, true));
        _ = dict1.TryAdd("bank", new HstsDomainPolicy("bank", expires365Days, true));
        _ = dict1.TryAdd("bing", new HstsDomainPolicy("bing", expires365Days, true));
        _ = dict1.TryAdd("boo", new HstsDomainPolicy("boo", expires365Days, true));
        _ = dict1.TryAdd("channel", new HstsDomainPolicy("channel", expires365Days, true));
        _ = dict1.TryAdd("chrome", new HstsDomainPolicy("chrome", expires365Days, true));
        // Segment size: 2
        _ = dict2.TryAdd("0--1.de", new HstsDomainPolicy("0--1.de", expires365Days, true));
        _ = dict2.TryAdd("0-0.io", new HstsDomainPolicy("0-0.io", expires365Days, true));
        _ = dict2.TryAdd("0-0.lt", new HstsDomainPolicy("0-0.lt", expires365Days, true));
        _ = dict2.TryAdd("0-24.com", new HstsDomainPolicy("0-24.com", expires365Days, true));
        _ = dict2.TryAdd("0-24.net", new HstsDomainPolicy("0-24.net", expires365Days, true));
        _ = dict2.TryAdd("0-9.com", new HstsDomainPolicy("0-9.com", expires365Days, true));
        _ = dict2.TryAdd("0.sb", new HstsDomainPolicy("0.sb", expires365Days, true));
        _ = dict2.TryAdd("00.eco", new HstsDomainPolicy("00.eco", expires365Days, true));
        _ = dict2.TryAdd("00010110.nl", new HstsDomainPolicy("00010110.nl", expires365Days, true));
        _ = dict2.TryAdd("0008.life", new HstsDomainPolicy("0008.life", expires365Days, true));
        // Segment size: 3
        _ = dict3.TryAdd("0.com.ms", new HstsDomainPolicy("0.com.ms", expires365Days, true));
        _ = dict3.TryAdd("0ii0.eu.org", new HstsDomainPolicy("0ii0.eu.org", expires365Days, true));
        _ = dict3.TryAdd("1-2-3bounce.co.uk", new HstsDomainPolicy("1-2-3bounce.co.uk", expires365Days, true));
        _ = dict3.TryAdd("100plus.com.my", new HstsDomainPolicy("100plus.com.my", expires365Days, true));
        _ = dict3.TryAdd("100plus.com.sg", new HstsDomainPolicy("100plus.com.sg", expires365Days, true));
        _ = dict3.TryAdd("101warehousing.com.au", new HstsDomainPolicy("101warehousing.com.au", expires365Days, true));
        _ = dict3.TryAdd("106.hi.cn", new HstsDomainPolicy("106.hi.cn", expires365Days, true));
        _ = dict3.TryAdd("11tv.dp.ua", new HstsDomainPolicy("11tv.dp.ua", expires365Days, true));
        _ = dict3.TryAdd("123host.com.au", new HstsDomainPolicy("123host.com.au", expires365Days, true));
        _ = dict3.TryAdd("123noticias.com.br", new HstsDomainPolicy("123noticias.com.br", expires365Days, true));
        // Segment size: 4
        _ = dict4.TryAdd("1.0.0.1", new HstsDomainPolicy("1.0.0.1", expires365Days, false));
        _ = dict4.TryAdd("1022996493.rsc.cdn77.org", new HstsDomainPolicy("1022996493.rsc.cdn77.org", expires126Days, true));
        _ = dict4.TryAdd("1464424382.rsc.cdn77.org", new HstsDomainPolicy("1464424382.rsc.cdn77.org", expires126Days, true));
        _ = dict4.TryAdd("1844329061.rsc.cdn77.org", new HstsDomainPolicy("1844329061.rsc.cdn77.org", expires126Days, true));
        _ = dict4.TryAdd("1972969867.rsc.cdn77.org", new HstsDomainPolicy("1972969867.rsc.cdn77.org", expires126Days, true));
        _ = dict4.TryAdd("agriculture.vic.gov.au", new HstsDomainPolicy("agriculture.vic.gov.au", expires365Days, true));
        _ = dict4.TryAdd("alanburr.us.eu.org", new HstsDomainPolicy("alanburr.us.eu.org", expires365Days, true));
        _ = dict4.TryAdd("allamakee.k12.ia.us", new HstsDomainPolicy("allamakee.k12.ia.us", expires365Days, true));
        _ = dict4.TryAdd("api.mega.co.nz", new HstsDomainPolicy("api.mega.co.nz", expires365Days, true));
        _ = dict4.TryAdd("armadale.wa.gov.au", new HstsDomainPolicy("armadale.wa.gov.au", expires365Days, true));
        // Segment size: 5
        _ = dict5.TryAdd("wnc-frontend-alb-1765173526.ap-northeast-2.elb.amazonaws.com", new HstsDomainPolicy("wnc-frontend-alb-1765173526.ap-northeast-2.elb.amazonaws.com", expires365Days, true));
    }
}