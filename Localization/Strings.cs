using System.Globalization;

namespace MftParser.Localization;

public static class Strings {
    public static bool IsEnglish { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "es";

    public static void ToggleLanguage() => IsEnglish = !IsEnglish;

    static string Pick(string es, string en) => IsEnglish ? en : es;

    public static string Count(long n) {
        var text = n.ToString("#,0", CultureInfo.InvariantCulture);
        return IsEnglish ? text : text.Replace(',', '.');
    }

    public static string Seconds(double s) => s.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    public static string LanguageButton => Pick("EN", "ES");
    public static string LanguageTip => Pick("Switch to English", "Cambiar a español");
    public static string ThemeButton(bool pink) => pink ? Pick("Oscuro", "Dark") : Pick("Rosa", "Pink");
    public static string ThemeTip => Pick("Cambiar el tema", "Change theme");
    public static string SidebarTip => Pick("Mostrar u ocultar la barra lateral", "Show or hide the sidebar");

    public static string SearchHint => Pick("Buscar nombre, ruta o #registro...", "Search name, path or #record...");
    public static string Parse => Pick("Parsear", "Parse");
    public static string OpenFileOption => Pick("Archivo $MFT...", "$MFT file...");
    public static string RecordsChip(long n) => Pick($"{Count(n)} registros", $"{Count(n)} records");
    public static string BootChip(DateTime boot) => $"Boot {boot.ToString(Pick("dd/MM HH:mm", "MM/dd HH:mm"), CultureInfo.InvariantCulture)}";
    public static string BootTip => Pick("Hora en que prendió la PC", "Time the PC was turned on");
    public static string TimestompChip(int n) => n == 1 ? Pick("1 posible timestomp", "1 possible timestomp") : Pick($"{Count(n)} posibles timestomp", $"{Count(n)} possible timestomp");
    public static string TimestompTip => Pick("Registros donde $SI no cuadra con $FN. Clic para filtrarlos", "Records where $SI does not match $FN. Click to filter them");

    public static string Range => Pick("RANGO", "RANGE");
    public static string FromBoot => Pick("Desde boot", "Since boot");
    public static string LastHour => Pick("Última hora", "Last hour");
    public static string Last24 => "24 h";
    public static string Everything => Pick("Todo", "All");
    public static string DateOf => Pick("Fecha de", "Date of");
    public static string RangeHint => "yyyy-MM-dd HH:mm:ss";
    public static string[] DateFields => IsEnglish
        ? new[] { "Any $SI date", "Created", "Modified", "MFT changed", "Accessed", "$FN created" }
        : new[] { "Cualquier fecha $SI", "Creación", "Modificación", "Cambio MFT", "Acceso", "$FN creación" };

    public static string Views => Pick("VISTAS", "VIEWS");
    public static string Extensions => Pick("EXTENSIONES", "EXTENSIONS");
    public static string Folders => Pick("CARPETAS", "FOLDERS");
    public static string HideNoise => Pick("Ocultar ruido de Windows", "Hide Windows noise");
    public static string HideNoiseTip => Pick("Oculta WinSxS, servicing, DriverStore, $Extend y otras carpetas que Windows llena solo", "Hides WinSxS, servicing, DriverStore, $Extend and other folders Windows fills on its own");
    public static string ShowFolders => Pick("Mostrar carpetas", "Show folders");

    public static string CatAll => Pick("Todo", "All");
    public static string CatDeleted => Pick("Borrados", "Deleted");
    public static string CatTimestomp => Pick("Posible timestomp", "Possible timestomp");
    public static string CatAds => Pick("Con ADS", "With ADS");
    public static string CatDownloaded => Pick("Descargados (Zone.Id)", "Downloaded (Zone.Id)");
    public static string CatHidden => Pick("Ocultos / sistema", "Hidden / system");
    public static string CatOrphans => Pick("Huérfanos", "Orphans");
    public static string CatScripts => ".bat / .cmd / .ps1 / .vbs";
    public static string CatArchives => ".zip / .rar / .7z";
    public static string CatPrefetch => ".pf (Prefetch)";
    public static string CatRecycle => Pick("Papelera ($Recycle.Bin)", "Recycle Bin");

    public static string AdsRows => Pick("ADS como filas", "ADS as rows");
    public static string AdsRowsTip => Pick(
        "Cada stream alternativo sale en su propia fila (archivo:stream), igual que en MFTECmd",
        "Every alternate stream gets its own row (file:stream), same as MFTECmd");
    public static string UtcTimes => Pick("Horas en UTC", "Times in UTC");

    public static string OpenFolder => Pick("Abrir carpeta", "Open folder");
    public static string Copy => Pick("Copiar", "Copy");
    public static string Copied => Pick("Copiado", "Copied");
    public static string CopyPath => Pick("Copiar ruta", "Copy path");
    public static string OpenInExplorer => Pick("Abrir ruta en el Explorador de archivos", "Open path in File Explorer");
    public static string PathCopied => Pick("Ruta copiada", "Path copied");
    public static string TabSummary => Pick("Resumen", "Summary");
    public static string TabAttributes => Pick("Atributos", "Attributes");
    public static string[] AttributeColumns => IsEnglish
        ? new[] { "TYPE", "ATTRIBUTE", "NAME", "FORM", "SIZE", "ID", "OFFSET", "DETAIL" }
        : new[] { "TIPO", "ATRIBUTO", "NOMBRE", "FORMA", "TAMAÑO", "ID", "OFFSET", "DETALLE" };
    public static string FormResident => Pick("Residente", "Resident");
    public static string FormNonResident => Pick("No residente", "Non-resident");
    public static string AttributesNote => Pick(
        "Atributos del registro base. Si hay registros de extensión, lo que guardan aparece debajo de 0x20 $ATTRIBUTE_LIST con el registro al que apunta.",
        "Attributes of the base record. If there are extension records, what they hold is listed under 0x20 $ATTRIBUTE_LIST with the record it points to.");
    public static string TabStreams => "Streams";
    public static string TabRecord => Pick("Registro", "Record");
    public static string SelectRecord => Pick("Seleccioná un registro", "Select a record");

    public static string KeyState => Pick("Estado", "State");
    public static string KeySize => Pick("Tamaño", "Size");
    public static string KeyData => "$DATA";
    public static string KeyShortName => Pick("Nombre 8.3", "8.3 name");
    public static string KeyParent => Pick("Carpeta padre", "Parent folder");
    public static string StateInUse => Pick("En uso", "In use");
    public static string StateDeleted => Pick("Borrado", "Deleted");
    public static string StateDirectory => Pick("Carpeta", "Folder");
    public static string Allocated(string size) => Pick($"asignado {size}", $"allocated {size}");
    public static string ResidentAllocated => Pick("residente", "resident");
    public static string DataResident => Pick("Residente en el registro", "Resident in the record");
    public static string DataRuns(int runs) => Pick($"No residente · {runs} run{(runs == 1 ? "" : "s")}", $"Non-resident · {runs} run{(runs == 1 ? "" : "s")}");
    public static string DataNone => Pick("Sin $DATA", "No $DATA");
    public static string RecordMeta(long index, int seq) => Pick($"registro {index}  seq {seq}", $"record {index}  seq {seq}");
    public static string ParentMeta(long index, int seq) => Pick($"padre {index}/{seq}", $"parent {index}/{seq}");

    public static string ReasonDeleted => Pick(
        "Registro libre (In Use = 0): el archivo se borró y la entrada todavía no se reutilizó",
        "Free record (In Use = 0): the file was deleted and the entry has not been reused yet");
    public static string ReasonSiBeforeFn => Pick("$SI creación es anterior a $FN creación", "$SI created is earlier than $FN created");
    public static string ReasonZeroFraction => Pick(
        "$SI creación con fracción en cero y $FN no (típico de SetFileTime / timestomp)",
        "$SI created has a zero fraction and $FN does not (typical of SetFileTime / timestomp)");
    public static string ReasonAdsRow(string stream, string owner) => Pick($"Fila de ADS: stream {stream} de {owner}", $"ADS row: stream {stream} of {owner}");
    public static string ReasonReparse(string target) => Pick($"Reparse point hacia {target}", $"Reparse point to {target}");
    public static string ReasonAds => Pick("Tiene streams alternativos (ADS)", "Has alternate data streams (ADS)");
    public static string ReasonOrphan => Pick(
        "Huérfano: la carpeta padre ya no existe o se reutilizó, la ruta no se puede armar",
        "Orphan: the parent folder no longer exists or was reused, the path cannot be rebuilt");
    public static string ReasonDownloaded(string host) => string.IsNullOrEmpty(host)
        ? Pick("Bajado de internet (Zone.Identifier)", "Downloaded from the internet (Zone.Identifier)")
        : Pick($"Bajado de {host}", $"Downloaded from {host}");

    public static string NoStreams => Pick("Sin streams alternativos, solo el $DATA sin nombre.", "No alternate streams, only the unnamed $DATA.");
    public static string StreamResident(string size) => Pick($"residente · {size}", $"resident · {size}");
    public static string StreamNonResident(string size) => Pick($"no residente · {size}", $"non-resident · {size}");
    public static string StreamNotRead => Pick("El contenido está fuera del registro y no se lee.", "The content lives outside the record and is not read.");
    public static string StreamBinary(string size) => Pick($"Contenido binario · {size}", $"Binary content · {size}");

    public static string HexHeader => Pick("Cabecera FILE", "FILE header");
    public static string HexOther => Pick("Otros (0x20, 0x40, 0x50, 0x90...)", "Others (0x20, 0x40, 0x50, 0x90...)");
    public static string HexNote => Pick("Registro crudo leído del disco, sin aplicar fixups.", "Raw record read from disk, fixups not applied.");
    public static string HexFailed(string message) => Pick($"No se pudo leer el registro: {message}", $"Could not read the record: {message}");

    public static string Status(long records, long deleted, int shown) => Pick(
        $"{Count(records)} registros · {Count(deleted)} borrado{(deleted == 1 ? "" : "s")} · mostrando {Count(shown)}",
        $"{Count(records)} records · {Count(deleted)} deleted · showing {Count(shown)}");
    public static string ParsedIn(TimeSpan t) => Pick($"Parseado en {Seconds(t.TotalSeconds)}", $"Parsed in {Seconds(t.TotalSeconds)}");
    public static string OrphansNote(int n) => n == 1 ? Pick("1 huérfano", "1 orphan") : Pick($"{Count(n)} huérfanos", $"{Count(n)} orphans");
    public static string TornNote(int n) => n == 1 ? Pick("1 registro dañado", "1 damaged record") : Pick($"{Count(n)} registros dañados", $"{Count(n)} damaged records");
    public static string AttributeListNote => Pick("$MFT con $ATTRIBUTE_LIST: puede faltar el final", "$MFT has an $ATTRIBUTE_LIST: the tail may be missing");
    public static string Filtering => Pick("filtrando...", "filtering...");
    public static string PathGone => Pick("La carpeta ya no existe en el disco", "The folder no longer exists on disk");

    public static string PickVolume => Pick("Elegí el volumen", "Pick the volume");
    public static string PickVolumeHint => Pick(
        "Se lee el $MFT en crudo desde el disco, sin copiarlo. Hay que abrirlo como administrador.",
        "The $MFT is read raw from the disk, without copying it. It has to run as administrator.");
    public static string OpenFile => Pick("Abrir archivo $MFT...", "Open $MFT file...");
    public static string ParseVolume(string label) => Pick($"Parsear {label}", $"Parse {label}");
    public static string NoVolumes => Pick("No hay volúmenes NTFS", "No NTFS volumes found");
    public static string LocalDisk => Pick("Disco local", "Local disk");
    public static string NoMft => Pick("sin MFT", "no MFT");
    public static string FileFilter => Pick("$MFT exportado|*.*", "Exported $MFT|*.*");

    public static string Reading(string label) => Pick($"Leyendo $MFT de {label}", $"Reading $MFT from {label}");
    public static string ReadingDetail(string done, string total, long records) => Pick(
        $"{done} de {total} · {Count(records)} registros",
        $"{done} of {total} · {Count(records)} records");
    public static string StepRead => Pick("Decodificando registros y fixups", "Decoding records and fixups");
    public static string StepPaths => Pick("Armando rutas", "Rebuilding paths");
    public static string StepFlags => Pick("Marcando borrados, timestomp y ADS", "Flagging deleted, timestomp and ADS");
    public static string Cancel => Pick("Cancelar", "Cancel");

    public static string ErrorAccess => Pick("Sin permisos para leer el disco: abrilo como administrador.", "No permission to read the disk: run it as administrator.");
    public static string ErrorNotNtfs => Pick("Ese volumen no es NTFS.", "That volume is not NTFS.");
    public static string ErrorNotMft => Pick("El archivo no parece un $MFT (no empieza con FILE).", "The file does not look like an $MFT (it does not start with FILE).");
    public static string ErrorGeneric(string message) => Pick($"No se pudo leer: {message}", $"Could not read: {message}");
}
