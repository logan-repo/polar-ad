namespace PolarAd.Core.Rules;

/// <summary>
/// Ad domains that are verified to be served from first-party hosts and that the
/// public lists miss. Shipped with the app and always applied; they cannot be
/// removed from the UI. Add an entry to the allowlist to make an exception.
/// </summary>
public static class BuiltInRules
{
    public static readonly string[] Domains =
    {
        // Ruliweb full-page background ad (view.js / adClick served from its own CDN host).
        "image.ruliweb.com",

        // Naver ad delivery, impression tracking and click relay.
        "tivan.naver.com",
        "g.tivan.naver.com",
        "veta.naver.com",
        "nam.veta.naver.com",
        "siape.veta.naver.com",
        "ader.naver.com",
        "wcs.naver.com",
        "recoshopping.naver.com",
        "shopsquare.naver.com",

        // Korean ad and tracking networks named in the earlier analysis.
        "wtg-ads.com",
        "acecounter.com",
        "netinsight.co.kr",
    };
}
