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
$rankSection = Require-Node "//w:Expander[@x:Name='WindowsDriverStoreExpander']" 'Windows local Driver Store evidence section missing'
if ($rankSection.GetAttribute('Visibility') -ne 'Collapsed' -or
    $rankSection.GetAttribute('IsExpanded') -ne 'False') {
    throw 'PnPUtil evidence must stay hidden until an inventory exists and collapsed by default.'
}
$rankDevice = Require-Node "//w:ComboBox[@x:Name='WindowsStoreDeviceCombo']" 'device picker for PnPUtil is missing'
if ($rankDevice.GetAttribute('ItemsSource') -ne '{Binding Drivers}') {
    throw 'PnPUtil device picker must bind only to live Windows driver inventory.'
}
$rankButton = Require-Node "//w:Button[@x:Name='WindowsStoreReadButton']" 'read-only PnPUtil action missing'
if ($rankButton.GetAttribute('Click') -ne 'WindowsStoreReadButton_Click' -or
    $null -eq $rankButton.SelectSingleNode('ancestor::w:Expander[@x:Name="WindowsDriverStoreExpander"]', $namespaces)) {
    throw 'PnPUtil command must be a direct opt-in from the local Driver Store section.'
}
$rankOutput = Require-Node "//w:TextBox[@x:Name='WindowsStoreEvidenceText']" 'local Windows rank view missing'
if ($rankOutput.GetAttribute('IsReadOnly') -ne 'True' -or
    $rankOutput.GetAttribute('Visibility') -ne 'Collapsed') {
    throw 'PnPUtil evidence must not be editable or shown before a manual request.'
}
$softwareReview = Require-Node "//w:Expander[@x:Name='RelatedSoftwareExpander']" 'vendor Software review missing'
if ($softwareReview.GetAttribute('Visibility') -ne 'Collapsed' -or
    $softwareReview.GetAttribute('IsExpanded') -ne 'False') {
    throw 'WUA Type=Software results must be hidden until searched and collapsed by default.'
}
$softwareRows = Require-Node "//w:ListView[@x:Name='RelatedSoftwareList']" 'WUA vendor software list missing'
if ($softwareRows.GetAttribute('ItemsSource') -ne '{Binding RelatedSoftwareHints}' -or
    $null -eq $softwareRows.SelectSingleNode('ancestor::w:Expander[@x:Name="RelatedSoftwareExpander"]', $namespaces)) {
    throw 'WUA software evidence must have separate informational binding, not WUA Driver updates.'
}
if ($updates.GetAttribute('ItemsSource') -eq $softwareRows.GetAttribute('ItemsSource')) {
    throw 'Companion Type=Software items must never be attached to the installable Driver list.'
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
# Visual refresh #140: buttons, scan state and source hierarchy must
# remain usable on compact windows without weakening the install policy.
$primaryButton = Require-Node "//w:Style[@x:Key='PrimaryButton']" 'primary button visual style missing'
$chrome = $primaryButton.SelectSingleNode('./w:Setter[@Property="Template"]/w:Setter.Value/w:ControlTemplate/w:Border[@x:Name="Chrome"]', $namespaces)
if ($null -eq $chrome -or $chrome.GetAttribute('CornerRadius') -ne '10') {
    throw 'Visual refresh: primary/secondary buttons must share rounded control template.'
}
$focus = $primaryButton.SelectSingleNode('.//w:Trigger[@Property="IsKeyboardFocused"]', $namespaces)
$disabled = $primaryButton.SelectSingleNode('.//w:Trigger[@Property="IsEnabled" and @Value="False"]', $namespaces)
$secondary = Require-Node "//w:Style[@x:Key='SecondaryButton']" 'secondary button style missing'
if ($null -eq $focus -or $null -eq $disabled -or
    $secondary.GetAttribute('BasedOn') -ne '{StaticResource PrimaryButton}') {
    throw 'Visual refresh: button keyboard focus, disabled state and shared styling are required.'
}

$scan = Require-Node "//w:Button[@x:Name='ScanAllButton']" 'primary scan action missing'
if ($scan.GetAttribute('Click') -ne 'ScanAllButton_Click') {
    throw 'Visual refresh: prominent scan action must keep the guided read-only handler.'
}
$progress = Require-Node "//w:ProgressBar[@x:Name='HomeScanProgressBar']" 'guided scan progress missing'
if ($progress.GetAttribute('Visibility') -ne 'Collapsed' -or
    $progress.GetAttribute('IsIndeterminate') -ne 'True' -or
    $null -eq $progress.SelectSingleNode('ancestor::w:ScrollViewer[@x:Name="HomePanel"]', $namespaces)) {
    throw 'Visual refresh: scanning progress must be hidden initially and remain inside Home.'
}

$emptyPanel = Require-Node "//w:StackPanel[@x:Name='CompactEmptyStatePanel']" 'result empty panel missing'
$emptyCopy = Require-Node "//w:TextBlock[@x:Name='CompactEmptyStateText']" 'result empty message missing'
if ($emptyPanel.GetAttribute('Visibility') -ne 'Collapsed' -or
    $null -eq $emptyCopy.SelectSingleNode('ancestor::w:StackPanel[@x:Name="CompactEmptyStatePanel"]', $namespaces)) {
    throw 'Visual refresh: empty explanation must show only for zero visible candidates.'
}
if ($null -eq $updates.SelectSingleNode('.//w:TextBlock[@Text="{Binding KindText}"]', $namespaces)) {
    throw 'Visual refresh: each WUA row must expose its kind separately from OEM results.'
}
$toolsSection = Require-Node "//w:Expander[@x:Name='VendorToolsExpander']" 'OEM diagnostic tools grouping missing'
if ($toolsSection.GetAttribute('IsExpanded') -ne 'False' -or
    $null -eq $localFolder.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorToolsExpander"]', $namespaces) -or
    $null -eq $softwareRows.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorToolsExpander"]', $namespaces) -or
    $null -ne $verified.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorToolsExpander"]', $namespaces)) {
    throw 'Visual refresh: diagnostic OEM tools must be opt-in while verified recommendations remain visible.'
}

$windowCode = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/MainWindow.xaml.cs') -Raw
if ($windowCode -notmatch 'HomeScanProgressBar.Visibility = Visibility.Visible' -or
    $windowCode -notmatch 'HomeScanProgressBar.Visibility = Visibility.Collapsed' -or
    $windowCode -notmatch 'CompactEmptyStatePanel.Visibility = visible == 0') {
    throw 'Visual refresh: live progress and empty state must be synchronized by code-behind.'
}

# Compact list navigation #142: local search, OEM space and visible-only
# selection. These static checks supplement xUnit and real Windows UI QA.
$search = Require-Node "//w:TextBox[@x:Name='CompactSearchTextBox']" 'compact result search missing'
if ($search.GetAttribute('TextChanged') -ne 'CompactSearchTextBox_TextChanged' -or
    $search.GetAttribute('AutomationProperties.Name') -notmatch 'Cerca') {
    throw 'Compact results: search must be accessible and wired to the live result filter.'
}
$searchParent = $search.SelectSingleNode('ancestor::w:Border[ancestor::w:Grid[@x:Name="HomeResultsPanel"]][1]', $namespaces)
if ($null -eq $searchParent -or $searchParent.GetAttribute('Grid.Row') -ne '1') {
    throw 'Compact results: search must stay next to the WUA kind filter.'
}
$vendorPanel = Require-Node "//w:Expander[@x:Name='VendorResultsExpander']" 'collapsible OEM results area missing'
if ($vendorPanel.GetAttribute('Grid.Row') -ne '3' -or
    $vendorPanel.GetAttribute('IsExpanded') -ne 'False' -or
    $null -eq $verified.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorResultsExpander"]', $namespaces) -or
    $null -eq $localFolder.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorResultsExpander"]', $namespaces)) {
    throw 'Compact results: OEM tools and recommendations must be under the collapsed source section.'
}

$selected = Require-Node "//w:Button[@x:Name='CompactUpdateSelectedButton']" 'selected update action missing'
if ($selected.GetAttribute('ToolTip') -notmatch 'visibili') {
    throw 'Compact results: selected update action must explain visible-only semantics.'
}
$all = Require-Node "//w:Button[@x:Name='CompactUpdateAllButton']" 'update all action missing'
if ($all.GetAttribute('ToolTip') -notmatch 'nascosti dal filtro') {
    throw 'Compact results: update all must disclose that it operates independently of filter visibility.'
}
if ($windowCode -notmatch 'CompactResultSearch.Matches' -or
    $windowCode -notmatch 'CompactSearchTextBox.Clear\(\)' -or
    $windowCode -notmatch 'CompactUpdatesView.Cast<SelectableDriverUpdate>\(\)' -or
    $windowCode -notmatch 'VendorResultsExpander.IsExpanded =') {
    throw 'Compact results: search, reset, visible selected updates and OEM auto-expand must be wired.'
}

# #149: Eight-category diagnostic evidence stays informational,
# behind both the OEM results expander and its own opt-in section.
$referenceExpander = Require-Node "//w:Expander[@x:Name='ReferenceCoverageExpander']" 'eight-component source coverage missing'
if ($referenceExpander.GetAttribute('Visibility') -ne 'Collapsed' -or
    $referenceExpander.GetAttribute('IsExpanded') -ne 'False' -or
    $referenceExpander.GetAttribute('Header') -notmatch 'NVIDIA' -or
    $null -eq $referenceExpander.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorResultsExpander"]', $namespaces)) {
    throw 'Reference source coverage: must be opt-in inside the non-installable OEM source section.'
}
$referenceRows = Require-Node "//w:ItemsControl[@x:Name='ReferenceCoverageList']" 'per-component diagnosis list missing'
if ($referenceRows.GetAttribute('ItemsSource') -ne '{Binding ReferenceCoverageRows}' -or
    $null -eq $referenceRows.SelectSingleNode('ancestor::w:Expander[@x:Name="ReferenceCoverageExpander"]', $namespaces)) {
    throw 'Reference source coverage: independent informational rows must bind to fixed-category diagnostics.'
}
foreach ($field in @('Component', 'PnpStatus', 'WindowsDriverStatus', 'SoftwareStatus', 'NextStep')) {
    $candidate = $referenceRows.SelectSingleNode(".//w:TextBlock[@Text='{Binding $field}']", $namespaces)
    if ($null -eq $candidate) {
        throw "Reference source coverage: missing visual explanation for $field."
    }
}
if ($windowCode -notmatch 'ReferenceCoveragePresentation.Create' -or
    $windowCode -notmatch 'ReferenceCoverageRows.Clear\(\)' -or
    $windowCode -notmatch 'RefreshReferenceCoverage\(\)' -or
    $null -ne $referenceRows.SelectSingleNode('ancestor::w:ListView[@x:Name="CompactUpdatesList"]', $namespaces)) {
    throw 'Reference source coverage: reset, refresh and WUA installation lists must remain separate.'
}

Write-Host 'Guided results UI static contract: passed.'
