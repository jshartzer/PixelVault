[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms

$appRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $appRoot 'GameCaptureManager.config.json'
$modulePath = Join-Path $appRoot 'GameCaptureManager.Core.psm1'
$toolsRoot = Join-Path $appRoot 'tools'
$logsRoot = Join-Path $appRoot 'logs'
$assetsRoot = Join-Path $appRoot 'assets'
$bundledExifToolPath = Join-Path $toolsRoot 'exiftool.exe'
$iconPath = Join-Path $assetsRoot 'PixelVault.ico'
$logoPath = Join-Path $assetsRoot 'PixelVault.png'

Import-Module $modulePath -Force

foreach ($directory in @($toolsRoot, $logsRoot)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
}

$script:sessionLogPath = Join-Path $logsRoot ("GameCaptureManager-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

$defaultConfig = [ordered]@{
    SourceFolder             = 'A:\Dropbox\Camera Uploads\Screenshot Upload'
    DestinationFolder        = 'A:\Dropbox\Game Captures'
    ExifToolPath             = $bundledExifToolPath
    OpenFolderAfterRun       = $true
    OpenDestinationAfterMove = $true
    RenameExtensions         = '.png, .jpg, .jpeg, .webp'
    MetadataExtensions       = '.png, .jpg, .jpeg'
    MoveExtensions           = '.jpg, .jpeg, .png, .mp4, .mkv, .avi, .mov, .wmv, .webm'
    AppIdRegex               = '(?<!\d)(\d{3,})(?!\d)'
    RecurseRename            = $true
    WhatIfRename             = $false
    WhatIfMetadata           = $false
    WhatIfMove               = $false
    AddGameCaptureKeywords   = $true
    MoveConflictMode         = 'Rename'
}

function Get-AppConfig {
    if (-not (Test-Path -LiteralPath $configPath)) {
        return [pscustomobject]$defaultConfig
    }

    try {
        $loaded = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    }
    catch {
        [System.Windows.MessageBox]::Show(
            "Could not read config file.`n`n$configPath`n`nUsing defaults instead.",
            'PixelVault'
        ) | Out-Null
        return [pscustomobject]$defaultConfig
    }

    foreach ($key in $defaultConfig.Keys) {
        if (-not ($loaded.PSObject.Properties.Name -contains $key)) {
            $loaded | Add-Member -NotePropertyName $key -NotePropertyValue $defaultConfig[$key]
        }
    }

    return $loaded
}

function Save-AppConfig([hashtable]$config) {
    $config | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
}

function Split-ExtensionsText([string]$value) {
    return @(
        $value -split ',' |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Add-Log([string]$message) {
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$timestamp] $message"
    $logTextBox.AppendText("$line`r`n")
    $logTextBox.ScrollToEnd()
    Add-Content -LiteralPath $script:sessionLogPath -Value $line
    $window.Dispatcher.Invoke([action]{}, [Windows.Threading.DispatcherPriority]::Background)
}

function New-BrowseHandler {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Controls.TextBox]$TextBox,

        [Parameter(Mandatory)]
        [ValidateSet('File', 'Folder')]
        [string]$Mode,

        [string]$Filter = 'All Files (*.*)|*.*'
    )

    return {
        if ($Mode -eq 'File') {
            $dialog = [Microsoft.Win32.OpenFileDialog]::new()
            $dialog.Filter = $Filter
            $dialog.InitialDirectory = if ((Test-Path -LiteralPath $TextBox.Text) -and -not (Get-Item -LiteralPath $TextBox.Text).PSIsContainer) {
                Split-Path -Parent $TextBox.Text
            }
            elseif (Test-Path -LiteralPath $TextBox.Text) {
                $TextBox.Text
            }
            else {
                $appRoot
            }

            if ($dialog.ShowDialog()) {
                $TextBox.Text = $dialog.FileName
            }
        }
        else {
            $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
            if (Test-Path -LiteralPath $TextBox.Text) {
                $dialog.SelectedPath = $TextBox.Text
            }

            if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                $TextBox.Text = $dialog.SelectedPath
            }
        }
    }.GetNewClosure()
}

[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="PixelVault"
        Height="940"
        Width="1120"
        MinHeight="820"
        MinWidth="980"
        WindowStartupLocation="CenterScreen"
        Background="#FFF3EEE4">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Padding="24" CornerRadius="22" Margin="0,0,0,16" Background="#FF161C20">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="16" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Border Width="72" Height="72" CornerRadius="20" Background="#FFF7F3EB" VerticalAlignment="Center">
                    <Image x:Name="HeaderLogoImage" Stretch="Uniform" Margin="10" />
                </Border>
                <StackPanel Grid.Column="2" VerticalAlignment="Center">
                    <TextBlock Text="PixelVault" FontSize="31" FontWeight="SemiBold" Foreground="#FFF8F3E7" />
                    <TextBlock Text="Archive, refine, and move your best game captures with a sharper workflow." Margin="0,8,0,0" FontSize="14" Foreground="#FFB7C6C0" />
                </StackPanel>
                <Border Grid.Column="3" Background="#FF20343A" CornerRadius="14" Padding="14,10" VerticalAlignment="Center">
                    <TextBlock x:Name="StatusTextBlock" Text="Ready" Foreground="#FFF8F3E7" FontSize="14" VerticalAlignment="Center" />
                </Border>
            </Grid>
        </Border>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="1.8*" />
                <ColumnDefinition Width="1.25*" />
            </Grid.ColumnDefinitions>

            <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto" Margin="0,0,16,0">
                <StackPanel>
                    <Border Padding="18" CornerRadius="18" Background="#FFFFFBF4" BorderBrush="#FFE2D8C5" BorderThickness="1" Margin="0,0,0,16">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>

                            <TextBlock Grid.Row="0" Grid.ColumnSpan="3" Text="Folders and tools" FontSize="19" FontWeight="SemiBold" Margin="0,0,0,14" Foreground="#FF1F2A30" />

                            <TextBlock Grid.Row="1" Grid.Column="0" Margin="0,0,12,12" VerticalAlignment="Center" Text="Source folder" />
                            <TextBox x:Name="SourceFolderTextBox" Grid.Row="1" Grid.Column="1" Margin="0,0,12,12" Padding="8" />
                            <Button x:Name="BrowseSourceFolderButton" Grid.Row="1" Grid.Column="2" Margin="0,0,0,12" Padding="14,8" Content="Browse" />

                            <TextBlock Grid.Row="2" Grid.Column="0" Margin="0,0,12,0" VerticalAlignment="Center" Text="Destination folder" />
                            <TextBox x:Name="DestinationFolderTextBox" Grid.Row="2" Grid.Column="1" Margin="0,0,12,0" Padding="8" />
                            <Button x:Name="BrowseDestinationFolderButton" Grid.Row="2" Grid.Column="2" Margin="0,0,0,0" Padding="14,8" Content="Browse" />
                        </Grid>
                    </Border>

                    <Border Padding="18" CornerRadius="18" Background="#FFFFFBF4" BorderBrush="#FFE2D8C5" BorderThickness="1" Margin="0,0,0,16">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>

                            <TextBlock Grid.Row="0" Grid.ColumnSpan="3" Text="Rename Steam files" FontSize="19" FontWeight="SemiBold" Margin="0,0,0,14" Foreground="#FF1F2A30" />

                            <TextBlock Grid.Row="1" Grid.Column="0" Margin="0,0,12,12" VerticalAlignment="Center" Text="Extensions" />
                            <TextBox x:Name="RenameExtensionsTextBox" Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,12" Padding="8" />

                            <TextBlock Grid.Row="2" Grid.Column="0" Margin="0,0,12,0" VerticalAlignment="Center" Text="AppID regex" />
                            <TextBox x:Name="AppIdRegexTextBox" Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,0" Padding="8" />
                        </Grid>
                    </Border>

                    <Border Padding="18" CornerRadius="18" Background="#FFFFFBF4" BorderBrush="#FFE2D8C5" BorderThickness="1" Margin="0,0,0,16">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>

                            <TextBlock Grid.Row="0" Grid.ColumnSpan="3" Text="Metadata settings" FontSize="19" FontWeight="SemiBold" Margin="0,0,0,14" Foreground="#FF1F2A30" />

                            <TextBlock Grid.Row="1" Grid.Column="0" Margin="0,0,12,12" VerticalAlignment="Center" Text="ExifTool" />
                            <TextBox x:Name="ExifToolPathTextBox" Grid.Row="1" Grid.Column="1" Margin="0,0,12,12" Padding="8" />
                            <Button x:Name="BrowseExifToolButton" Grid.Row="1" Grid.Column="2" Margin="0,0,0,12" Padding="14,8" Content="Browse" />

                            <TextBlock Grid.Row="2" Grid.Column="0" Margin="0,0,12,0" VerticalAlignment="Center" Text="Extensions" />
                            <TextBox x:Name="MetadataExtensionsTextBox" Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,0" Padding="8" />
                        </Grid>
                    </Border>

                    <Border Padding="18" CornerRadius="18" Background="#FFFFFBF4" BorderBrush="#FFE2D8C5" BorderThickness="1">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>

                            <TextBlock Grid.Row="0" Grid.ColumnSpan="2" Text="Move settings" FontSize="19" FontWeight="SemiBold" Margin="0,0,0,14" Foreground="#FF1F2A30" />

                            <TextBlock Grid.Row="1" Grid.Column="0" Margin="0,0,12,0" VerticalAlignment="Center" Text="Extensions" />
                            <TextBox x:Name="MoveExtensionsTextBox" Grid.Row="1" Grid.Column="1" Padding="8" />
                        </Grid>
                    </Border>
                </StackPanel>
            </ScrollViewer>

            <Grid Grid.Column="1">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="220" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <Border Grid.Row="0" Padding="18" CornerRadius="18" Background="#FFF5E4CE" BorderBrush="#FFE2C6A7" BorderThickness="1" Margin="0,0,0,16">
                    <StackPanel>
                        <TextBlock Text="Actions" FontSize="19" FontWeight="SemiBold" Margin="0,0,0,14" Foreground="#FF553824" />
                        <WrapPanel>
                            <Button x:Name="RunAllButton" Content="Run Full Workflow" Padding="18,10" Margin="0,0,12,12" Background="#FF275D47" Foreground="White" />
                            <Button x:Name="PreviewButton" Content="Preview Counts" Padding="18,10" Margin="0,0,12,12" />
                            <Button x:Name="RenameButton" Content="Rename" Padding="18,10" Margin="0,0,12,12" />
                            <Button x:Name="MetadataButton" Content="Metadata" Padding="18,10" Margin="0,0,12,12" />
                            <Button x:Name="MoveButton" Content="Move" Padding="18,10" Margin="0,0,12,12" />
                            <Button x:Name="SaveConfigButton" Content="Save Settings" Padding="18,10" Margin="0,0,12,12" />
                            <Button x:Name="OpenLogsButton" Content="Open Logs Folder" Padding="18,10" Margin="0,0,12,12" />
                        </WrapPanel>
                    </StackPanel>
                </Border>

                <Border Grid.Row="1" Padding="18" CornerRadius="18" Background="#FFFFFBF4" BorderBrush="#FFE2D8C5" BorderThickness="1" Margin="0,0,0,16">
                    <StackPanel>
                        <TextBlock Text="Live options" FontSize="19" FontWeight="SemiBold" Margin="0,0,0,14" Foreground="#FF1F2A30" />
                        <CheckBox x:Name="RecurseRenameCheckBox" Content="Search subfolders when renaming" Margin="0,0,0,8" />
                        <CheckBox x:Name="WhatIfRenameCheckBox" Content="Preview rename only" Margin="0,0,0,8" />
                        <CheckBox x:Name="WhatIfMetadataCheckBox" Content="Preview metadata update only" Margin="0,0,0,8" />
                        <CheckBox x:Name="AddKeywordsCheckBox" Content="Add &quot;Game Capture&quot; keywords" Margin="0,0,0,8" />
                        <CheckBox x:Name="WhatIfMoveCheckBox" Content="Preview move only" Margin="0,0,0,8" />
                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                            <TextBlock Text="Move conflicts" VerticalAlignment="Center" Margin="0,0,12,0" />
                            <ComboBox x:Name="MoveConflictModeComboBox" Width="140">
                                <ComboBoxItem Content="Rename" />
                                <ComboBoxItem Content="Overwrite" />
                                <ComboBoxItem Content="Skip" />
                            </ComboBox>
                        </StackPanel>
                        <CheckBox x:Name="OpenSourceAfterRunCheckBox" Content="Open source folder after run" Margin="0,0,0,8" />
                        <CheckBox x:Name="OpenDestinationAfterMoveCheckBox" Content="Open destination folder after move" Margin="0,0,0,0" />
                    </StackPanel>
                </Border>

                <Border Grid.Row="2" Padding="14" CornerRadius="18" Background="#FFF8F1E5" BorderBrush="#FFE2D8C5" BorderThickness="1" Margin="0,0,0,16">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
                        <TextBlock Text="Preview summary" FontSize="19" FontWeight="SemiBold" Foreground="#FF1F2A30" Margin="0,0,0,8" />
                        <TextBox x:Name="PreviewTextBox"
                                 Grid.Row="1"
                                 IsReadOnly="True"
                                 AcceptsReturn="True"
                                 TextWrapping="Wrap"
                                 VerticalScrollBarVisibility="Auto"
                                 BorderThickness="0"
                                 Background="#FFF8F1E5"
                                 Foreground="#FF3A3025"
                                 FontFamily="Cascadia Mono" />
                    </Grid>
                </Border>

                <Border Grid.Row="3" Padding="14" CornerRadius="18" Background="#FF12191E">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
                        <TextBlock Text="Activity log" FontSize="19" FontWeight="SemiBold" Foreground="#FFF1E9DA" Margin="0,0,0,8" />
                        <TextBox x:Name="LogTextBox"
                                 Grid.Row="1"
                                 IsReadOnly="True"
                                 AcceptsReturn="True"
                                 TextWrapping="Wrap"
                                 VerticalScrollBarVisibility="Auto"
                                 HorizontalScrollBarVisibility="Auto"
                                 BorderThickness="0"
                                 Background="#FF12191E"
                                 Foreground="#FFF1E9DA"
                                 FontFamily="Cascadia Mono" />
                    </Grid>
                </Border>
            </Grid>
        </Grid>

        <TextBlock Grid.Row="2"
                   Margin="4,14,0,0"
                   Foreground="#FF6A625A"
                   Text="PixelVault portable build: settings apply immediately, and branding assets live in the assets folder." />
    </Grid>
</Window>
"@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)

$statusTextBlock = $window.FindName('StatusTextBlock')
$headerLogoImage = $window.FindName('HeaderLogoImage')
$sourceFolderTextBox = $window.FindName('SourceFolderTextBox')
$destinationFolderTextBox = $window.FindName('DestinationFolderTextBox')
$exifToolPathTextBox = $window.FindName('ExifToolPathTextBox')
$renameExtensionsTextBox = $window.FindName('RenameExtensionsTextBox')
$metadataExtensionsTextBox = $window.FindName('MetadataExtensionsTextBox')
$moveExtensionsTextBox = $window.FindName('MoveExtensionsTextBox')
$appIdRegexTextBox = $window.FindName('AppIdRegexTextBox')
$browseSourceFolderButton = $window.FindName('BrowseSourceFolderButton')
$browseDestinationFolderButton = $window.FindName('BrowseDestinationFolderButton')
$browseExifToolButton = $window.FindName('BrowseExifToolButton')
$runAllButton = $window.FindName('RunAllButton')
$previewButton = $window.FindName('PreviewButton')
$renameButton = $window.FindName('RenameButton')
$metadataButton = $window.FindName('MetadataButton')
$moveButton = $window.FindName('MoveButton')
$saveConfigButton = $window.FindName('SaveConfigButton')
$openLogsButton = $window.FindName('OpenLogsButton')
$recurseRenameCheckBox = $window.FindName('RecurseRenameCheckBox')
$whatIfRenameCheckBox = $window.FindName('WhatIfRenameCheckBox')
$whatIfMetadataCheckBox = $window.FindName('WhatIfMetadataCheckBox')
$addKeywordsCheckBox = $window.FindName('AddKeywordsCheckBox')
$whatIfMoveCheckBox = $window.FindName('WhatIfMoveCheckBox')
$moveConflictModeComboBox = $window.FindName('MoveConflictModeComboBox')
$openSourceAfterRunCheckBox = $window.FindName('OpenSourceAfterRunCheckBox')
$openDestinationAfterMoveCheckBox = $window.FindName('OpenDestinationAfterMoveCheckBox')
$previewTextBox = $window.FindName('PreviewTextBox')
$logTextBox = $window.FindName('LogTextBox')

$config = Get-AppConfig

if (Test-Path -LiteralPath $iconPath) {
    $window.Icon = [System.Windows.Media.Imaging.BitmapFrame]::Create([Uri]$iconPath)
}

if (Test-Path -LiteralPath $logoPath) {
    $headerLogoImage.Source = [System.Windows.Media.Imaging.BitmapFrame]::Create([Uri]$logoPath)
}

if ((-not $config.ExifToolPath) -or (-not (Test-Path -LiteralPath $config.ExifToolPath))) {
    if (Test-Path -LiteralPath $bundledExifToolPath) {
        $config.ExifToolPath = $bundledExifToolPath
    }
}

function Set-UiFromConfig($configObject) {
    $sourceFolderTextBox.Text = $configObject.SourceFolder
    $destinationFolderTextBox.Text = $configObject.DestinationFolder
    $exifToolPathTextBox.Text = $configObject.ExifToolPath
    $renameExtensionsTextBox.Text = $configObject.RenameExtensions
    $metadataExtensionsTextBox.Text = $configObject.MetadataExtensions
    $moveExtensionsTextBox.Text = $configObject.MoveExtensions
    $appIdRegexTextBox.Text = $configObject.AppIdRegex
    $recurseRenameCheckBox.IsChecked = [bool]$configObject.RecurseRename
    $whatIfRenameCheckBox.IsChecked = [bool]$configObject.WhatIfRename
    $whatIfMetadataCheckBox.IsChecked = [bool]$configObject.WhatIfMetadata
    $addKeywordsCheckBox.IsChecked = [bool]$configObject.AddGameCaptureKeywords
    $whatIfMoveCheckBox.IsChecked = [bool]$configObject.WhatIfMove
    foreach ($item in $moveConflictModeComboBox.Items) {
        if ($item.Content -eq $configObject.MoveConflictMode) {
            $moveConflictModeComboBox.SelectedItem = $item
            break
        }
    }
    if (-not $moveConflictModeComboBox.SelectedItem) {
        $moveConflictModeComboBox.SelectedIndex = 0
    }
    $openSourceAfterRunCheckBox.IsChecked = [bool]$configObject.OpenFolderAfterRun
    $openDestinationAfterMoveCheckBox.IsChecked = [bool]$configObject.OpenDestinationAfterMove
}

Set-UiFromConfig -configObject $config

function Get-UiConfig {
    return [ordered]@{
        SourceFolder             = $sourceFolderTextBox.Text.Trim()
        DestinationFolder        = $destinationFolderTextBox.Text.Trim()
        ExifToolPath             = $exifToolPathTextBox.Text.Trim()
        OpenFolderAfterRun       = [bool]$openSourceAfterRunCheckBox.IsChecked
        OpenDestinationAfterMove = [bool]$openDestinationAfterMoveCheckBox.IsChecked
        RenameExtensions         = $renameExtensionsTextBox.Text.Trim()
        MetadataExtensions       = $metadataExtensionsTextBox.Text.Trim()
        MoveExtensions           = $moveExtensionsTextBox.Text.Trim()
        AppIdRegex               = $appIdRegexTextBox.Text.Trim()
        RecurseRename            = [bool]$recurseRenameCheckBox.IsChecked
        WhatIfRename             = [bool]$whatIfRenameCheckBox.IsChecked
        WhatIfMetadata           = [bool]$whatIfMetadataCheckBox.IsChecked
        WhatIfMove               = [bool]$whatIfMoveCheckBox.IsChecked
        AddGameCaptureKeywords   = [bool]$addKeywordsCheckBox.IsChecked
        MoveConflictMode         = [string]$moveConflictModeComboBox.Text
    }
}

function Save-CurrentConfig {
    $current = Get-UiConfig
    Save-AppConfig -config $current
    Add-Log "Saved settings to $configPath"
    $statusTextBlock.Text = 'Settings saved'
}

function Set-UiState([bool]$isBusy) {
    $controls = @(
        $browseSourceFolderButton,
        $browseDestinationFolderButton,
        $browseExifToolButton,
        $runAllButton,
        $previewButton,
        $renameButton,
        $metadataButton,
        $moveButton,
        $saveConfigButton,
        $openLogsButton
    )

    foreach ($control in $controls) {
        $control.IsEnabled = -not $isBusy
    }
}

function Set-PreviewText([string]$text) {
    $previewTextBox.Text = $text
}

function Show-WorkflowPreview {
    $current = Get-UiConfig

    try {
        Test-PathSetting -Label 'Source folder' -Path $current.SourceFolder
        $preview = Get-WorkflowPreview `
            -SourceFolder $current.SourceFolder `
            -DestinationFolder $current.DestinationFolder `
            -RenameExtensions (Split-ExtensionsText $current.RenameExtensions) `
            -MetadataExtensions (Split-ExtensionsText $current.MetadataExtensions) `
            -MoveExtensions (Split-ExtensionsText $current.MoveExtensions) `
            -AppIdRegex $current.AppIdRegex `
            -RecurseRename:([bool]$current.RecurseRename)

        $summary = @(
            "Rename: $($preview.RenameCandidates) candidate(s) out of $($preview.RenameScanned) scanned"
            "Metadata: $($preview.MetadataCandidates) candidate(s) out of $($preview.MetadataScanned) scanned"
            "Move: $($preview.MoveCandidates) candidate(s)"
            "Move conflicts in destination: $($preview.MoveConflicts)"
            ""
            "Rename samples:"
            ($(if ($preview.SampleRenameCandidates.Count) { $preview.SampleRenameCandidates -join [Environment]::NewLine } else { '(none)' }))
            ""
            "Metadata samples:"
            ($(if ($preview.SampleMetadataTargets.Count) { $preview.SampleMetadataTargets -join [Environment]::NewLine } else { '(none)' }))
            ""
            "Move samples:"
            ($(if ($preview.SampleMoveCandidates.Count) { $preview.SampleMoveCandidates -join [Environment]::NewLine } else { '(none)' }))
        ) -join [Environment]::NewLine

        Set-PreviewText -text $summary
        Add-Log "Preview refreshed. Rename=$($preview.RenameCandidates), Metadata=$($preview.MetadataCandidates), Move=$($preview.MoveCandidates), Conflicts=$($preview.MoveConflicts)"
        $statusTextBlock.Text = 'Preview ready'
    }
    catch {
        Set-PreviewText -text $_.Exception.Message
        Add-Log "Preview failed. $($_.Exception.Message)"
        $statusTextBlock.Text = 'Preview failed'
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'PixelVault') | Out-Null
    }
}

function Test-PathSetting {
    param(
        [string]$Label,
        [string]$Path,
        [switch]$AllowMissingFolder
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Label is empty."
    }

    if ($AllowMissingFolder) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label not found: $Path"
    }
}

function Test-ExifToolReady {
    param(
        [Parameter(Mandatory)]
        [string]$ExifToolPath
    )

    Test-PathSetting -Label 'ExifTool' -Path $ExifToolPath

    $exifToolFolder = Split-Path -Parent $ExifToolPath
    $exifToolSupportFolder = Join-Path $exifToolFolder 'exiftool_files'

    if (([IO.Path]::GetFileName($ExifToolPath)).ToLowerInvariant() -eq 'exiftool.exe' -and -not (Test-Path -LiteralPath $exifToolSupportFolder)) {
        throw "ExifTool is missing its support folder. Expected: $exifToolSupportFolder"
    }

    $versionOutput = & $ExifToolPath -ver 2>&1
    if ($LASTEXITCODE -ne 0) {
        $details = ($versionOutput | ForEach-Object { [string]$_ }) -join ' '
        throw "ExifTool failed to start. $details"
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('rename', 'metadata', 'move')]
        [string]$Step,

        [Parameter(Mandatory)]
        [hashtable]$CurrentConfig
    )

    switch ($Step) {
        'rename' {
            Test-PathSetting -Label 'Source folder' -Path $CurrentConfig.SourceFolder
            Add-Log 'Starting rename step.'
            $statusTextBlock.Text = 'Running rename'
            $result = Rename-SteamCaptureFiles `
                -Path $CurrentConfig.SourceFolder `
                -Recurse:([bool]$CurrentConfig.RecurseRename) `
                -Extensions (Split-ExtensionsText $CurrentConfig.RenameExtensions) `
                -AppIdRegex $CurrentConfig.AppIdRegex `
                -WhatIf:([bool]$CurrentConfig.WhatIfRename) `
                -Logger ${function:Add-Log}
            Add-Log "Rename summary: renamed $($result.Renamed), skipped $($result.Skipped)."
        }
        'metadata' {
            Test-PathSetting -Label 'Source folder' -Path $CurrentConfig.SourceFolder
            Test-ExifToolReady -ExifToolPath $CurrentConfig.ExifToolPath
            Add-Log 'Starting metadata step.'
            $statusTextBlock.Text = 'Running metadata'
            $result = Update-CaptureMetadata `
                -TargetFolder $CurrentConfig.SourceFolder `
                -ExifToolPath $CurrentConfig.ExifToolPath `
                -Extensions (Split-ExtensionsText $CurrentConfig.MetadataExtensions) `
                -IncludeGameCaptureKeywords:([bool]$CurrentConfig.AddGameCaptureKeywords) `
                -WhatIf:([bool]$CurrentConfig.WhatIfMetadata) `
                -Logger ${function:Add-Log}
            Add-Log "Metadata summary: updated $($result.Updated), skipped $($result.Skipped)."
        }
        'move' {
            Test-PathSetting -Label 'Source folder' -Path $CurrentConfig.SourceFolder
            Test-PathSetting -Label 'Destination folder' -Path $CurrentConfig.DestinationFolder -AllowMissingFolder
            Add-Log 'Starting move step.'
            $statusTextBlock.Text = 'Running move'
            $result = Move-CaptureMediaFiles `
                -SourceFolder $CurrentConfig.SourceFolder `
                -DestinationFolder $CurrentConfig.DestinationFolder `
                -Extensions (Split-ExtensionsText $CurrentConfig.MoveExtensions) `
                -ConflictMode $CurrentConfig.MoveConflictMode `
                -WhatIf:([bool]$CurrentConfig.WhatIfMove) `
                -Logger ${function:Add-Log}
            Add-Log "Move summary: moved $($result.Moved), skipped $($result.Skipped), renamed-on-conflict $($result.RenamedOnConflict)."
        }
    }
}

function Invoke-AppWorkflow {
    param([string[]]$Steps)

    $current = Get-UiConfig
    Set-UiState -isBusy $true
    Save-AppConfig -config $current

    try {
        foreach ($step in $Steps) {
            Invoke-Step -Step $step -CurrentConfig $current
        }

        $statusTextBlock.Text = 'Workflow complete'
        Add-Log 'Workflow complete.'

        if ($current.OpenFolderAfterRun -and (Test-Path -LiteralPath $current.SourceFolder)) {
            Start-Process explorer.exe $current.SourceFolder
        }

        if (($Steps -contains 'move') -and $current.OpenDestinationAfterMove -and (Test-Path -LiteralPath $current.DestinationFolder)) {
            Start-Process explorer.exe $current.DestinationFolder
        }
    }
    catch {
        $statusTextBlock.Text = 'Workflow failed'
        Add-Log $_.Exception.Message
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'PixelVault') | Out-Null
    }
    finally {
        Set-UiState -isBusy $false
    }
}

$browseSourceFolderButton.Add_Click((New-BrowseHandler -TextBox $sourceFolderTextBox -Mode Folder))
$browseDestinationFolderButton.Add_Click((New-BrowseHandler -TextBox $destinationFolderTextBox -Mode Folder))
$browseExifToolButton.Add_Click((New-BrowseHandler -TextBox $exifToolPathTextBox -Mode File -Filter 'Executables (*.exe)|*.exe|All Files (*.*)|*.*'))

$saveConfigButton.Add_Click({ Save-CurrentConfig })
$previewButton.Add_Click({ Show-WorkflowPreview })
$openLogsButton.Add_Click({ Start-Process explorer.exe $logsRoot })
$renameButton.Add_Click({ Invoke-AppWorkflow -Steps @('rename') })
$metadataButton.Add_Click({ Invoke-AppWorkflow -Steps @('metadata') })
$moveButton.Add_Click({ Invoke-AppWorkflow -Steps @('move') })
$runAllButton.Add_Click({ Invoke-AppWorkflow -Steps @('rename', 'metadata', 'move') })

Add-Log 'App ready.'
Add-Log "Session log: $script:sessionLogPath"
if (Test-Path -LiteralPath $bundledExifToolPath) {
    Add-Log "Bundled ExifTool available at $bundledExifToolPath"
}
if (Test-Path -LiteralPath (Join-Path $toolsRoot 'exiftool_files')) {
    Add-Log "Bundled ExifTool support folder available at $(Join-Path $toolsRoot 'exiftool_files')"
}
Show-WorkflowPreview
[void]$window.ShowDialog()
