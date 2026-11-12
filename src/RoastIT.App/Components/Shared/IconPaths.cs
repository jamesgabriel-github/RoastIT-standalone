namespace RoastIT.App.Components.Shared;

/// <summary>
/// Icon paths on a 20 × 20 viewBox, ported from the prototypes (shared-ui-spec §9).
/// Drawn with no fill and a currentColor stroke by <see cref="Icon"/>.
/// </summary>
public static class IconPaths
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["flame"] = """<path d="M10 2.5c2 3 4.5 5 4.5 8.5a4.5 4.5 0 01-9 0c0-1.3.5-2.2 1.2-3.1.2 1 .9 1.6 1.6 1.6.9 0 1.3-.8 1.1-1.6-.3-1.3-.6-2.6.6-5.4z"></path>""",
        ["list"] = """<line x1="4" y1="6" x2="16" y2="6"></line><line x1="4" y1="10" x2="16" y2="10"></line><line x1="4" y1="14" x2="12" y2="14"></line>""",
        ["gear"] = """<circle cx="10" cy="10" r="3"></circle><line x1="10" y1="2.5" x2="10" y2="4.5"></line><line x1="10" y1="15.5" x2="10" y2="17.5"></line><line x1="2.5" y1="10" x2="4.5" y2="10"></line><line x1="15.5" y1="10" x2="17.5" y2="10"></line><line x1="4.9" y1="4.9" x2="6.3" y2="6.3"></line><line x1="13.7" y1="13.7" x2="15.1" y2="15.1"></line><line x1="4.9" y1="15.1" x2="6.3" y2="13.7"></line><line x1="13.7" y1="6.3" x2="15.1" y2="4.9"></line>""",
        ["plus"] = """<line x1="10" y1="4" x2="10" y2="16"></line><line x1="4" y1="10" x2="16" y2="10"></line>""",
        ["close"] = """<line x1="5" y1="5" x2="15" y2="15"></line><line x1="15" y1="5" x2="5" y2="15"></line>""",
        ["back"] = """<polyline points="12,4 6,10 12,16"></polyline>""",
        ["search"] = """<circle cx="8.5" cy="8.5" r="5"></circle><line x1="12.5" y1="12.5" x2="17" y2="17"></line>""",
        ["check"] = """<polyline points="4,10.5 8,14.5 16,5.5"></polyline>""",
        ["chevron"] = """<polyline points="8,4 13,10 8,16"></polyline>""",
        ["chevLeft"] = """<polyline points="12,4 7,10 12,16"></polyline>""",
        ["chevDown"] = """<polyline points="5,8 10,13 15,8"></polyline>""",
        ["chevUp"] = """<polyline points="5,12 10,7 15,12"></polyline>""",
        ["viewCards"] = """<rect x="3.5" y="3.5" width="13" height="5.5" rx="1.5"></rect><rect x="3.5" y="11" width="13" height="5.5" rx="1.5"></rect>""",
        ["viewCompact"] = """<line x1="3.5" y1="5" x2="16.5" y2="5"></line><line x1="3.5" y1="10" x2="16.5" y2="10"></line><line x1="3.5" y1="15" x2="16.5" y2="15"></line>""",
        ["calendar"] = """<rect x="3.5" y="4.5" width="13" height="12" rx="1.5"></rect><line x1="3.5" y1="8" x2="16.5" y2="8"></line><line x1="7" y1="2.5" x2="7" y2="5.5"></line><line x1="13" y1="2.5" x2="13" y2="5.5"></line>""",
        ["clock"] = """<circle cx="10" cy="10" r="6.5"></circle><polyline points="10,6.5 10,10 12.5,11.5"></polyline>""",
        ["person"] = """<circle cx="10" cy="7" r="3"></circle><path d="M4 16.5c0-3 2.7-5 6-5s6 2 6 5"></path>""",
        ["sortAsc"] = """<line x1="10" y1="16" x2="10" y2="4"></line><polyline points="5,9 10,4 15,9"></polyline>""",
        ["sortDesc"] = """<line x1="10" y1="4" x2="10" y2="16"></line><polyline points="5,11 10,16 15,11"></polyline>""",
        ["lock"] = """<rect x="4" y="9" width="12" height="8.5" rx="2"></rect><path d="M6.5 9V6.5a3.5 3.5 0 017 0V9"></path>""",
        ["key"] = """<circle cx="7" cy="12" r="3.5"></circle><path d="M9.5 9.5l7-7"></path><path d="M14 5l2 2"></path>""",
        ["sun"] = """<circle cx="10" cy="10" r="3.2"></circle><line x1="10" y1="2" x2="10" y2="3.8"></line><line x1="10" y1="16.2" x2="10" y2="18"></line><line x1="2" y1="10" x2="3.8" y2="10"></line><line x1="16.2" y1="10" x2="18" y2="10"></line><line x1="4.3" y1="4.3" x2="5.6" y2="5.6"></line><line x1="14.4" y1="14.4" x2="15.7" y2="15.7"></line><line x1="4.3" y1="15.7" x2="5.6" y2="14.4"></line><line x1="14.4" y1="5.6" x2="15.7" y2="4.3"></line>""",
        ["moon"] = """<path d="M16 12.2A6.5 6.5 0 017.8 4a6.5 6.5 0 108.2 8.2z"></path>""",
        ["device"] = """<rect x="5.5" y="2.5" width="9" height="15" rx="2"></rect><line x1="9" y1="15" x2="11" y2="15"></line>""",
        ["download"] = """<path d="M10 3v10"></path><polyline points="6,9.5 10,13.5 14,9.5"></polyline><path d="M4 16.5h12"></path>""",
        ["upload"] = """<path d="M10 14V4"></path><polyline points="6,7.5 10,3.5 14,7.5"></polyline><path d="M4 16.5h12"></path>""",
        ["warn"] = """<path d="M10 3l7.5 13h-15z"></path><line x1="10" y1="8" x2="10" y2="11.5"></line><line x1="10" y1="14" x2="10" y2="14.2"></line>""",
        ["alert"] = """<path d="M10 3.2l7.2 12.6H2.8z"></path><line x1="10" y1="8.3" x2="10" y2="11.4"></line><line x1="10" y1="13.9" x2="10" y2="13.95"></line>""",
        ["file"] = """<path d="M5 2.5h6.5L15 6v11.5H5z"></path><polyline points="11.5,2.5 11.5,6 15,6"></polyline><line x1="7.5" y1="10" x2="12.5" y2="10"></line><line x1="7.5" y1="13" x2="12.5" y2="13"></line>""",
        // App-only (not in the prototypes): the CSV card's Share/Open button (settings spec §10 #4).
        ["share"] = """<path d="M10 12.5V3"></path><polyline points="6.5,6.5 10,3 13.5,6.5"></polyline><path d="M6 9H4.5v8h11V9H14"></path>""",
        ["copy"] = """<rect x="6.5" y="6.5" width="10" height="10" rx="2"></rect><path d="M13.5 6.5V5a1.5 1.5 0 00-1.5-1.5H5A1.5 1.5 0 003.5 5v7A1.5 1.5 0 005 13.5h1.5"></path>""",
        ["backspace"] = """<path d="M7 5h9.5v10H7l-4.5-5z"></path><line x1="10" y1="8" x2="14" y2="12"></line><line x1="14" y1="8" x2="10" y2="12"></line>""",
    };

    public static string Get(string name) =>
        Paths.TryGetValue(name, out var paths) ? paths : string.Empty;

    public static bool Has(string name) => Paths.ContainsKey(name);
}
