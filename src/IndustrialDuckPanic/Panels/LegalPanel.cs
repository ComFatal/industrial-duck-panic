using System.Windows;
using System.Windows.Controls;

namespace IndustrialDuckPanic.Panels;

/// <summary>Full legal text: Fenris Creations (formerly CCP Games) notice, terms of use, privacy.</summary>
public sealed class LegalPanel : StackPanel
{
    public static readonly (string Title, string[] Paragraphs)[] Sections =
    [
        ("EVE Online intellectual property",
        [
            "EVE Online and the EVE logo are the registered trademarks of Fenris Creations (formerly CCP Games, CCP hf.). All rights are reserved worldwide. All other trademarks are the property of their respective owners.",
            "EVE Online, the EVE logo, EVE and all associated logos and designs are the intellectual property of Fenris Creations. All artwork, screenshots, characters, ships and ship names, vehicles, storylines, world facts or other recognizable features of the intellectual property relating to these trademarks are likewise the intellectual property of Fenris Creations. This includes the ORE (Outer Ring Excavations) logo used as this application's icon, and the names Porpoise, Orca and Rorqual.",
            "Fenris Creations has granted permission to third-party developers to use EVE Online and all associated logos and designs for promotional and information purposes, but does not endorse, and is not in any way affiliated with, Industrial Duck Panic. Fenris Creations is in no way responsible for the content on or functioning of this application, nor can it be liable for any damage arising from the use of this application.",
        ]),
        ("Terms of use",
        [
            "Industrial Duck Panic is a free, unofficial tool provided \"as is\", without warranty of any kind. Use it at your own risk. The authors are not liable for any loss, in game or outside the game, including lost ships, missed alerts or Discord messages that fail to arrive.",
            "The application never interacts with the EVE Online client: it does not send keys, clicks or chat messages, does not read the game's memory and does not automate anything in game. It only reads the log files the game writes on your disk and the data EVE's official API (ESI) gives with your permission. Alerts are sent only to Discord, never in game.",
            "You must respect the EVE Online End User License Agreement and Terms of Service, and the rules of the Discord servers you post to. The owner of a Discord webhook is responsible for who receives the IDP.config file.",
        ]),
        ("Privacy",
        [
            "Your EVE login happens on the official EVE website. The application receives only a token with three read-only rights: location, ship type, online status. It is stored on your computer, encrypted for your Windows account.",
            "Your character name, ship and solar system are sent only to the Discord webhook of the configuration you loaded, inside the alert messages. Nothing is sent to the authors of the application.",
            "You can remove your data at any time: Character ? Disconnect, then uninstall the application. You can also revoke the access on the EVE website (Account management ? Third party applications).",
        ]),
    ];

    public LegalPanel()
    {
        foreach (var (title, paragraphs) in Sections)
        {
            var section = new StackPanel();
            section.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("H2"), Margin = new Thickness(0) });
            foreach (var p in paragraphs)
                section.Children.Add(new TextBlock { Text = p, Style = (Style)Application.Current.FindResource("Body"), Margin = new Thickness(0, 8, 0, 0) });
            Children.Add(new Border { Style = (Style)Application.Current.FindResource("Section"), Child = section });
        }

        var version = typeof(LegalPanel).Assembly.GetName().Version?.ToString(3) ?? "?";
        Children.Add(new TextBlock
        {
            Text = $"Industrial Duck Panic {version}",
            Style = (Style)Application.Current.FindResource("Muted"),
            Margin = new Thickness(2, 0, 0, 0),
        });
    }
}
