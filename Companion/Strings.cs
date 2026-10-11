namespace CodexHebdo;

internal static class Strings
{
    internal static readonly Dictionary<string,string> English = new()
    {
        ["Codex Hebdo - Options"] = "Codex Hebdo - Settings",
        ["Aperçu en direct dans la barre des tâches · Annuler restaure les réglages enregistrés."] = "Live preview in the taskbar · Cancel restores your saved settings.",
        ["Fermer"] = "Close", ["Appliquer"] = "Apply", ["Annuler"] = "Cancel", ["Par défaut"] = "Defaults",
        ["Textes"] = "Text", ["Hebdo"] = "Weekly", ["Pourcentage restant"] = "Remaining percentage", ["Réinitialisations ↻"] = "Resets ↻",
        ["Afficher"] = "Show", ["Gras"] = "Bold", ["Italique"] = "Italic", ["Couleur automatique"] = "Automatic color",
        ["Taille du texte (DIP)"] = "Text size (DIP)", ["Couleur personnalisée"] = "Custom color", ["Logo"] = "Logo",
        ["Afficher le logo"] = "Show logo", ["Taille du logo (DIP)"] = "Logo size (DIP)", ["Logo d’origine"] = "Default logo",
        ["Logo personnalisé enregistré dans l’application"] = "Custom logo stored in the application",
        ["Importer un PNG / SVG…"] = "Import PNG / SVG…", ["Choisir un logo"] = "Choose a logo",
        ["Logo PNG ou SVG|*.png;*.svg"] = "PNG or SVG logo|*.png;*.svg", ["Import du logo"] = "Import logo",
        ["Teinter le logo personnalisé (sinon : couleurs d’origine)"] = "Tint custom logo (otherwise: original colors)",
        ["Couleur du logo"] = "Logo color", ["Logo d’origine / thème Windows"] = "Default logo / Windows theme",
        ["Disposition"] = "Layout", ["Espace entre le logo et le texte"] = "Space between logo and text",
        ["Espace entre le % et ↻"] = "Space between % and ↻", ["Espace entre les deux lignes"] = "Space between lines",
        ["Centrer Hebdo sur le pourcentage seulement"] = "Center Weekly above the percentage only",
        ["Par défaut, Hebdo est centré au-dessus de l’ensemble « %  ↻ »."] = "By default, Weekly is centered above the entire “%  ↻” row.",
        ["Couleurs auto"] = "Auto colors",
        ["Pourcentage : vert > 50 %, jaune > 20 %, rouge ≤ 20 %.\nRéinitialisations : rouge à 0, vert à partir de 1.\nUne information indisponible reste neutre."] = "Percentage: green > 50%, yellow > 20%, red ≤ 20%.\nResets: red at 0, green at 1 or more.\nUnavailable information stays neutral.",
        ["Vert"] = "Green", ["Jaune"] = "Yellow", ["Rouge"] = "Red", ["Général"] = "General",
        ["Actualiser maintenant"] = "Refresh now", ["Démarrer avec Windows"] = "Start with Windows",
        ["Ouverture des options : double-clic uniquement.\nUn clic simple ou un clic droit n’ouvre aucun menu.\nLes modifications non appliquées sont annulées à la fermeture."] = "Open settings with a double-click only.\nSingle-click and right-click do not open a menu.\nUnapplied changes are discarded when you close this window.",
        ["Langue / Language"] = "Language / Langue",
        ["Impossible d’enregistrer les réglages : "] = "Could not save settings: ",
        ["Logo invalide."] = "Invalid logo.", ["Logo trop volumineux (maximum 2 Mo)."] = "Logo is too large (maximum 2 MB).",
        ["SVG non reconnu."] = "Unrecognized SVG.", ["Dimensions du SVG invalides."] = "Invalid SVG dimensions.",
        ["PNG non reconnu."] = "Unrecognized PNG.", ["Choisissez un PNG de 4096 × 4096 pixels maximum."] = "Choose a PNG no larger than 4096 × 4096 pixels.",
        ["Utilisez un SVG statique, sans images ni styles externes."] = "Use a static SVG without images or external styles.",
        ["Le SVG contient une référence externe non prise en charge."] = "The SVG contains an unsupported external reference."
    };
    internal static string Get(string text, string language) => language == "en" && English.TryGetValue(text,out var translated) ? translated : text;
    internal static void Translate(Control control, string language)
    {
        if (control is TextBoxBase or UpDownBase or ComboBox) return;
        {
            if (control.Tag is not string) control.Tag = control.Text;
            control.Text = Get((string)control.Tag,language);
        }
        foreach (Control child in control.Controls) Translate(child,language);
    }
}
