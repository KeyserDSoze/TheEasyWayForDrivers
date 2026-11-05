#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

# Static WPF UI contract: catches structural regressions without a GUI runner.
# This does not replace visual QA at 100/125/150% Windows DPI.
$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$xamlPath = Join-Path $repositoryRoot 'src/App2.Desktop/MainWindow.xaml'
[xml]$document = Get-Content -LiteralPath $xamlPath -Raw
$namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
$namespaces.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')

function Require-Node([string]$xpath, [string]$message) {
    $node = $document.SelectSingleNode($xpath, $namespaces)
    if ($null -eq $node) {
        throw "Guided results UI contract failed: $message"
    }
    return $node
}

$resultsPanel = Require-Node "//w:Grid[@x:Name='HomeResultsPanel']" 'guided results panel missing'
$rows = $resultsPanel.SelectNodes('./w:Grid.RowDefinitions/w:RowDefinition', $namespaces)
if ($rows.Count -ne 5 -or $rows[2].GetAttribute('Height') -ne '*' -or
    $rows[3].GetAttribute('Height') -ne 'Auto') {
    throw 'Guided results: update list must occupy the flexible space, OEM handoffs must not take half the screen.'
}

$filters = Require-Node "//w:ComboBox[@x:Name='ResultFilterComboBox']" 'filter missing'
$firstFilter = $filters.SelectSingleNode('./w:ComboBoxItem[1]', $namespaces)
if ($null -eq $firstFilter -or $firstFilter.GetAttribute('Tag') -ne 'all') {
    throw 'Guided results: default filter must show all WUA candidates.'
}

$updates = Require-Node "//w:ListView[@x:Name='CompactUpdatesList']" 'WUA candidate list missing'
if ($updates.GetAttribute('ItemsSource') -ne '{Binding CompactUpdatesView}') {
    throw 'Guided results: WUA list must bind to CompactUpdatesView.'
}

$verified = Require-Node "//w:ListView[@x:Name='VendorChecksList']" 'verified OEM list missing'
if ($verified.GetAttribute('ItemsSource') -ne '{Binding VendorApplicableView}') {
    throw 'Guided results: ONLY manufacturer-assessed OEM updates should be shown by default.'
}

$expander = Require-Node "//w:Expander[@x:Name='OtherVendorSourcesExpander']" 'unverified sources expander missing'
if ($expander.GetAttribute('IsExpanded') -ne 'False') {
    throw 'Guided results: unverified device/vendor handoffs must be collapsed by default.'
}

$handoffs = Require-Node "//w:ListView[@x:Name='VendorHandoffsList']" 'vendor handoff list missing'
if ($handoffs.GetAttribute('ItemsSource') -ne '{Binding VendorHandoffsView}' -or
    $null -eq $handoffs.SelectSingleNode('ancestor::w:Expander[@x:Name="OtherVendorSourcesExpander"]', $namespaces)) {
    throw 'Guided results: unverified handoffs must stay inside the collapsed expander.'
}

$null = Require-Node "//w:Button[@x:Name='CompactUpdateAllButton']" 'Update All action missing'
$null = Require-Node "//w:Button[@x:Name='CopyCoverageReportButton']" 'copyable QA report missing'
$pcSpecialist = Require-Node "//w:Button[@x:Name='PcSpecialistPortalButton']" 'PCSpecialist driver portal missing'
if ($pcSpecialist.GetAttribute('Visibility') -ne 'Collapsed') {
    throw 'Guided results: PCSpecialist portal must be hidden unless the actual OEM matches.'
}
$localZip = Require-Node "//w:Button[@x:Name='CompareLocalOemZipButton']" 'local OEM archive comparison action missing'
if ($localZip.GetAttribute('Visibility') -ne 'Collapsed') {
    throw 'Local OEM comparison must stay disabled until inventory exists.'
}
$localResult = Require-Node "//w:Expander[@x:Name='LocalOemComparisonExpander']" 'local comparison findings section missing'
$localRows = Require-Node "//w:ListView[@x:Name='LocalOemFindingsList']" 'local OEM findings view missing'
if ($localRows.GetAttribute('ItemsSource') -ne '{Binding LocalOemFindings}' -or
    $null -eq $localRows.SelectSingleNode('ancestor::w:Expander[@x:Name="LocalOemComparisonExpander"]', $namespaces)) {
    throw 'ZIP findings must be bound only to the non-installable local OEM section.'
}
if ($localZip.GetAttribute('Click') -ne 'CompareLocalOemZipButton_Click') {
    throw 'Local OEM comparison button must invoke the explicit read-only flow.'
}
if ($localZip.GetAttribute('Content') -notmatch 'ZIP / CAB') {
    throw 'Local OEM comparison must make CAB support visible to the user.'
}
$localFolder = Require-Node "//w:Button[@x:Name='CompareLocalOemFolderButton']" 'OEM folder compare action missing'
if ($localFolder.GetAttribute('Visibility') -ne 'Collapsed' -or
    $localFolder.GetAttribute('Click') -ne 'CompareLocalOemFolderButton_Click') {
    throw 'OEM folder comparison must be gated behind a guided scan and explicit consent.'
}
if ($null -eq $localFolder.SelectSingleNode('ancestor::w:Border', $namespaces)) {
    throw 'OEM folder comparison must belong to the non-installable source section.'
}
$verifyOemCat = Require-Node "//w:Button[@x:Name='VerifyOemFolderCatalogButton']" 'read-only OEM CAT/INF review button missing'
if ($verifyOemCat.GetAttribute('Visibility') -ne 'Collapsed' -or
    $verifyOemCat.GetAttribute('Click') -ne 'VerifyOemFolderCatalogButton_Click') {
    throw 'OEM CAT/INF audit must be disabled by default and opt-in only.'
}
if ($verifyOemCat.GetAttribute('Content') -notmatch 'firme e file driver') {
    throw 'OEM signature action must clearly announce referenced payload checks.'
}
if ($null -eq $verifyOemCat.SelectSingleNode('ancestor::w:Expander[@x:Name="LocalOemComparisonExpander"]', $namespaces)) {
    throw 'OEM trust review must appear in the non-installable local OEM section.'
}
$null = Require-Node "//w:TextBlock[@x:Name='LocalOemCatalogAuditText']" 'audit status text missing'
if ($localResult.GetAttribute('Header') -notmatch 'non installabile') {
    throw 'Local OEM CAB/ZIP findings must be labelled non-installable.'
}
Write-Host 'Guided results UI static contract: passed.'
