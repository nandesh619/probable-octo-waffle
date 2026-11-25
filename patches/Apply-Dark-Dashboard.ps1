$ErrorActionPreference = 'Stop'
$xamlPath = 'SshAdbHub/MainWindow.xaml'
$codePath = 'SshAdbHub/MainWindow.xaml.cs'
$dashPath = 'SshAdbHub/MainWindow.Dashboard.cs'
$csprojPath = 'SshAdbHub/SshAdbHub.csproj'
$profileXamlPath = 'SshAdbHub/ProfileDialog.xaml'
$dialogsPath = 'SshAdbHub/Dialogs.cs'

$xaml = Get-Content $xamlPath -Raw

# 1. Remove any legacy HomeTab ("Dashboard") if present
$xaml = [regex]::Replace($xaml, '(?s)\s*<TabItem x:Name="HomeTab"[^>]*>.*?</TabItem>', '')

# 2. Move Router dashboard (DashTab) to index 0 (first TabItem inside MainTabs)
$dashPattern = '(?s)[ \t]*(?:<!-- ===== Router dashboard ===== -->\s*)?<TabItem x:Name="DashTab"[^>]*>.*?</TabItem>'
$dashMatch = [regex]::Match($xaml, $dashPattern)
if (-not $dashMatch.Success) { throw 'DashTab not found in MainWindow.xaml.' }
$dashBlock = $dashMatch.Value.TrimStart("`r", "`n")
$xaml = $xaml.Remove($dashMatch.Index, $dashMatch.Length)

$tabRegex = [regex]'(?s)<TabControl x:Name="MainTabs"[^>]*>'
$tabMatch = $tabRegex.Match($xaml)
if (-not $tabMatch.Success) { throw 'MainTabs TabControl not found in MainWindow.xaml.' }
$xaml = $xaml.Substring(0, $tabMatch.Index + $tabMatch.Length) + [Environment]::NewLine + $dashBlock + [Environment]::NewLine + $xaml.Substring($tabMatch.Index + $tabMatch.Length)
Set-Content $xamlPath $xaml -Encoding UTF8

# 3. Install ssh-adb-hub-app-icon.ico as app.ico and wire into .csproj + ProfileDialog.xaml
$iconSrc = @(
    'ssh-adb-hub-app-icon.ico',
    'assets/ssh-adb-hub-app-icon.ico',
    'SshAdbHub/app.ico'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iconSrc) { throw 'Missing ssh-adb-hub-app-icon.ico' }
if ((Resolve-Path $iconSrc).Path -ne (Resolve-Path 'SshAdbHub/app.ico' -ErrorAction SilentlyContinue).Path) {
    Copy-Item $iconSrc 'SshAdbHub/app.ico' -Force
}

$csproj = Get-Content $csprojPath -Raw
if ($csproj -notmatch '<ApplicationIcon>') {
    $csproj = $csproj.Replace('<Version>0.2.0</Version>', '<Version>0.2.0</Version>' + [Environment]::NewLine + '    <ApplicationIcon>app.ico</ApplicationIcon>')
}
if ($csproj -notmatch 'Include="app\.ico"') {
    $csproj = $csproj.Replace('</Project>', '  <ItemGroup>' + [Environment]::NewLine + '    <Resource Include="app.ico" />' + [Environment]::NewLine + '  </ItemGroup>' + [Environment]::NewLine + [Environment]::NewLine + '</Project>')
}
Set-Content $csprojPath $csproj -Encoding UTF8 -NoNewline

# 4. Theme ProfileDialog.xaml in Vantage dark palette with app.ico
$profileXaml = @'
<Window x:Class="SshAdbHub.ProfileDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="SSH profile" Icon="app.ico" Width="480" SizeToContent="Height"
        Background="#11141B" Foreground="#F1F5F9"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False">
    <Window.Resources>
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="#CBD5E1"/>
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#161A23"/>
            <Setter Property="Foreground" Value="#F1F5F9"/>
            <Setter Property="CaretBrush" Value="#B8E936"/>
            <Setter Property="BorderBrush" Value="#262D3D"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="8,6"/>
            <Setter Property="MinHeight" Value="32"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
        </Style>
        <Style TargetType="PasswordBox">
            <Setter Property="Background" Value="#161A23"/>
            <Setter Property="Foreground" Value="#F1F5F9"/>
            <Setter Property="CaretBrush" Value="#B8E936"/>
            <Setter Property="BorderBrush" Value="#262D3D"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="8,6"/>
            <Setter Property="MinHeight" Value="32"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
        </Style>
        <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="#CBD5E1"/>
        </Style>
        <Style TargetType="Button">
            <Setter Property="Background" Value="#181D27"/>
            <Setter Property="Foreground" Value="#E2E8F0"/>
            <Setter Property="BorderBrush" Value="#2A3244"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="12,5"/>
            <Setter Property="MinHeight" Value="32"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>
    </Window.Resources>
    <StackPanel Margin="16">
        <TextBlock Text="Profile name"/>
        <TextBox x:Name="NameBox" Margin="0,4,0,10"/>

        <Grid Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="90"/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0" Margin="0,0,8,0">
                <TextBlock Text="Host / IP"/>
                <TextBox x:Name="HostBox" Margin="0,4,0,0"/>
            </StackPanel>
            <StackPanel Grid.Column="1">
                <TextBlock Text="Port"/>
                <TextBox x:Name="PortBox" Margin="0,4,0,0"/>
            </StackPanel>
        </Grid>

        <TextBlock Text="Username"/>
        <TextBox x:Name="UserBox" Margin="0,4,0,10"/>

        <TextBlock Text="Private key file (OpenSSH format, optional - leave empty for password login)"/>
        <DockPanel Margin="0,4,0,10">
            <Button DockPanel.Dock="Right" Content="Browse..." Margin="6,0,0,0" Click="Browse_Click"/>
            <TextBox x:Name="KeyBox"/>
        </DockPanel>

        <TextBlock Text="Password (or key passphrase). Leave blank to keep the saved one."/>
        <DockPanel Margin="0,4,0,6">
            <Button DockPanel.Dock="Right" Content="Clear saved" Margin="6,0,0,0" Click="ClearSecret_Click"/>
            <PasswordBox x:Name="PassBox"/>
        </DockPanel>
        <TextBlock x:Name="SecretHint" Foreground="#8E98AB" Margin="0,0,0,10"/>

        <CheckBox x:Name="AutoChk" Content="Auto-connect when the app starts" Margin="0,0,0,14"/>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="OK" IsDefault="True" Width="84" Margin="0,0,8,0" Background="#B8E936" Foreground="#0B0D12" BorderBrush="#B8E936" Click="Ok_Click"/>
            <Button Content="Cancel" IsCancel="True" Width="84"/>
        </StackPanel>
    </StackPanel>
</Window>
'@
Set-Content $profileXamlPath $profileXaml -Encoding UTF8

# 5. Update MainWindow.Dashboard.cs status colors for dark background (never use black SystemColors.ControlTextBrush on dark cards)
$dashCode = Get-Content $dashPath -Raw
$dashCode = $dashCode.Replace('Brushes.DarkOrange', 'new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))')
$dashCode = $dashCode.Replace('Brushes.SeaGreen', 'new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))')
$dashCode = $dashCode.Replace('Brushes.Firebrick', 'new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71))')
$dashCode = $dashCode.Replace('SystemColors.ControlTextBrush', 'new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1))')
Set-Content $dashPath $dashCode -Encoding UTF8

# 6. Auto-refresh Router dashboard immediately on SSH connect when DashTab is the active landing tab
$code = Get-Content $codePath -Raw
$code = [regex]::Replace($code, '(?s)\s*private void HomeNavigate_Click\(object sender, RoutedEventArgs e\)\s*\{.*?\}\s*', [Environment]::NewLine)
if ($code -notmatch 'ReferenceEquals\(MainTabs\.SelectedItem, DashTab\)\)\s*_\s*=\s*RefreshDashboardAsync\(\)') {
    $code = $code.Replace('Log($"Connected to {_connectedLabel}");', 'Log($"Connected to {_connectedLabel}");' + [Environment]::NewLine + '            if (ReferenceEquals(MainTabs.SelectedItem, DashTab)) _ = RefreshDashboardAsync();')
}
Set-Content $codePath $code -Encoding UTF8

# 7. Theme Dialogs.cs prompt windows in dark obsidian + lime
$dialogs = Get-Content $dialogsPath -Raw
if ($dialogs -notmatch '0x11, 0x14, 0x1B') {
    $dialogs = $dialogs.Replace('using System.Windows.Controls;', 'using System.Windows.Controls;' + [Environment]::NewLine + 'using System.Windows.Media;')
    $dialogs = $dialogs.Replace('ResizeMode = ResizeMode.NoResize,', 'Background = new SolidColorBrush(Color.FromRgb(0x11, 0x14, 0x1B)),' + [Environment]::NewLine + '        Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),' + [Environment]::NewLine + '        ResizeMode = ResizeMode.NoResize,')
    $dialogs = $dialogs.Replace('ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };',
        'ok = new Button { Content = "OK", IsDefault = true, Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0xB8, 0xE9, 0x36)), Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x12)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xE9, 0x36)), FontWeight = FontWeights.SemiBold };')
    $dialogs = $dialogs.Replace('var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };',
        'var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80, Height = 30, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1D, 0x27)), Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x44)) };')
    $dialogs = $dialogs.Replace('var tb = new TextBox { Text = def, Margin = new Thickness(0, 6, 0, 0), MinWidth = 340 };',
        'var tb = new TextBox { Text = def, Margin = new Thickness(0, 6, 0, 0), MinWidth = 340, Padding = new Thickness(8, 5), Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1A, 0x23)), Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2D, 0x3D)) };')
    $dialogs = $dialogs.Replace('var tb = new TextBox { Text = def, MinWidth = 380 };',
        'var tb = new TextBox { Text = def, MinWidth = 380, Padding = new Thickness(8, 5), Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1A, 0x23)), Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2D, 0x3D)) };')
    Set-Content $dialogsPath $dialogs -Encoding UTF8
}

Write-Host 'Moved Router dashboard to landing slot, removed Dashboard home tab, wired app.ico, and applied dark theme accents.'
