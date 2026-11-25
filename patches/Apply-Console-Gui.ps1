$ErrorActionPreference = 'Stop'
$xamlPath = 'SshAdbHub/MainWindow.xaml'
$codePath = 'SshAdbHub/MainWindow.xaml.cs'
$xaml = Get-Content $xamlPath -Raw

# 1. Update Console (SSH) layout: clean labeled buttons, multiline input, full-width/height terminal output
$newConsole = @'
                    <DockPanel Grid.Row="0" LastChildFill="True">
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                            <Button x:Name="ConsoleRunBtn" Content="▶  Run commands" MinWidth="116" Click="ConsoleRun_Click"/>
                            <Button Content="■  Stop" MinWidth="76" Click="ConsoleStop_Click"/>
                            <Button Content="Clear output" MinWidth="96" Click="ConsoleClear_Click"/>
                        </StackPanel>
                        <TextBox x:Name="ConsoleInput" MinHeight="72" Height="72" Padding="8,7"
                                 AcceptsReturn="True" AcceptsTab="True" TextWrapping="NoWrap"
                                 VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                                 FontFamily="Consolas" ToolTip="Paste one command per line. Click Run commands or press Ctrl+Enter to send to the shell."
                                 KeyDown="ConsoleInput_KeyDown"/>
                    </DockPanel>
                    <TextBlock Grid.Row="1" Text="SSH TERMINAL  •  Persistent session — current directory and shell state are retained."
                               Margin="4,8,4,2" FontSize="11" FontWeight="SemiBold" Foreground="#718096"/>
                    <TextBox x:Name="ConsoleOut" Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="99" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" MinHeight="420" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>
'@

$consoleMarker = '<!-- ===== Console ===== -->'
$markerIndex = $xaml.IndexOf($consoleMarker, [System.StringComparison]::Ordinal)
if ($markerIndex -lt 0) { throw 'Console tab marker not found.' }
$beforeConsole = $xaml.Substring(0, $markerIndex)
$consolePart = $xaml.Substring($markerIndex + $consoleMarker.Length)
$pattern = '(?s)<DockPanel Grid.Row="0">.*?</DockPanel>\s*<TextBox x:Name="ConsoleOut"[^>]*/>'
$consolePart = [regex]::Replace($consolePart, $pattern, $newConsole.Trim(), 1)
$consoleRows = '<Grid.RowDefinitions>' + [Environment]::NewLine + '                        <RowDefinition Height="Auto"/>' + [Environment]::NewLine + '                        <RowDefinition Height="Auto"/>' + [Environment]::NewLine + '                        <RowDefinition Height="*"/>' + [Environment]::NewLine + '                    </Grid.RowDefinitions>'
$consolePart = [regex]::Replace($consolePart, '<Grid.RowDefinitions>.*?</Grid.RowDefinitions>', [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $consoleRows }, 1)
if ($consolePart -notmatch 'ConsoleClear_Click') { throw 'Console XAML replacement failed.' }
$xaml = $beforeConsole + $consoleMarker + $consolePart

# 2. Stretch ScriptOut, LogBox, and other output boxes across available space
$xaml = $xaml.Replace('<TextBox x:Name="ScriptOut" Grid.Row="3" Style="{StaticResource OutBox}"/>',
                      '<TextBox x:Name="ScriptOut" Grid.Row="3" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" MinHeight="240" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>')
$xaml = $xaml.Replace('<TextBox x:Name="LogBox" Margin="4" Style="{StaticResource OutBox}"/>',
                      '<TextBox x:Name="LogBox" Margin="4" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Style="{StaticResource OutBox}"/>')
$xaml = $xaml.Replace('<TextBox x:Name="AdbOut" Grid.Row="5" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>',
                      '<TextBox x:Name="AdbOut" Grid.Row="5" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>')
$xaml = $xaml.Replace('<TextBox x:Name="AppOut" Grid.Row="4" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>',
                      '<TextBox x:Name="AppOut" Grid.Row="4" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>')
$xaml = $xaml.Replace('<TextBox x:Name="HistoryOut" Grid.Row="2" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>',
                      '<TextBox x:Name="HistoryOut" Grid.Row="2" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Margin="0,4,0,0" Style="{StaticResource OutBox}"/>')

# 3. Window sizing and TabControl stretch
$xaml = $xaml.Replace('Title="SSH + ADB Hub" Height="780" Width="1180" MinHeight="520" MinWidth="860"', 'Title="SSH + ADB Hub" Height="900" Width="1440" MinHeight="680" MinWidth="1040"')

$xaml = [regex]::Replace($xaml, '<TabControl x:Name="MainTabs"([^>]*)>', {
    param($m)
    $attrs = $m.Groups[1].Value
    foreach ($name in @('HorizontalContentAlignment','VerticalContentAlignment','HorizontalAlignment','VerticalAlignment')) {
        $attributePattern = '(?<![\w:.-])' + [regex]::Escape($name) + '="[^"]*"'
        if ([regex]::IsMatch($attrs, $attributePattern)) {
            $attrs = [regex]::Replace($attrs, $attributePattern, ($name + '="Stretch"'), 1)
        } else {
            $attrs += ' ' + $name + '="Stretch"'
        }
    }
    '<TabControl x:Name="MainTabs"' + $attrs + '>'
}, 1)

if ($xaml -notmatch 'TabStripPlacement="Left"') {
    $xaml = [regex]::Replace($xaml, '<TabControl x:Name="MainTabs"([^>]*)>', {
        param($m)
        '<TabControl x:Name="MainTabs"' + $m.Groups[1].Value + ' TabStripPlacement="Left" Background="#F4F5FA" BorderThickness="0">' +
        '<TabControl.Resources><Style TargetType="{x:Type TabItem}">' +
        '<Setter Property="Width" Value="168"/><Setter Property="Padding" Value="16,13"/>' +
        '<Setter Property="Margin" Value="6,4"/><Setter Property="HorizontalContentAlignment" Value="Left"/>' +
        '<Setter Property="FontSize" Value="14"/><Setter Property="Foreground" Value="#667085"/>' +
        '<Setter Property="Background" Value="Transparent"/><Setter Property="BorderThickness" Value="0"/>' +
        '<Style.Triggers><Trigger Property="IsSelected" Value="True">' +
        '<Setter Property="Foreground" Value="#5B4AE8"/><Setter Property="Background" Value="#EEEAFE"/>' +
        '<Setter Property="FontWeight" Value="SemiBold"/></Trigger></Style.Triggers></Style></TabControl.Resources>'
    }, 1)
}

$xaml = $xaml.Replace('<Window x:Class="SshAdbHub.MainWindow"', '<Window x:Class="SshAdbHub.MainWindow" Background="#F4F5FA" Foreground="#20243A"')
$xaml = $xaml.Replace('<Setter Property="Margin" Value="3"/>', '<Setter Property="Margin" Value="4,3"/>')
$xaml = $xaml.Replace('<Setter Property="Padding" Value="10,4"/>', '<Setter Property="Padding" Value="12,6"/>' + [Environment]::NewLine + '            <Setter Property="MinHeight" Value="30"/>')

$theme = @'
        <SolidColorBrush x:Key="AccentBrush" Color="#6557D9"/>
        <SolidColorBrush x:Key="AccentSoftBrush" Color="#EEEAFE"/>
        <SolidColorBrush x:Key="PageBrush" Color="#F4F5FA"/>
        <SolidColorBrush x:Key="CardBrush" Color="#FFFFFF"/>
        <SolidColorBrush x:Key="LineBrush" Color="#E4E6EF"/>
        <SolidColorBrush x:Key="TextBrush" Color="#24243A"/>
        <SolidColorBrush x:Key="MutedBrush" Color="#74778B"/>
        <Style TargetType="{x:Type Button}">
            <Setter Property="Background" Value="#FFFFFF"/>
            <Setter Property="Foreground" Value="#45416F"/>
            <Setter Property="BorderBrush" Value="#DDD9F3"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="13,7"/>
            <Setter Property="Margin" Value="4"/>
            <Setter Property="MinHeight" Value="34"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Background" Value="#EEEAFE"/>
                    <Setter Property="BorderBrush" Value="#BDB5F0"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                    <Setter Property="Background" Value="#DED8FC"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.48"/>
                </Trigger>
            </Style.Triggers>
        </Style>
        <Style TargetType="{x:Type TextBox}">
            <Setter Property="Background" Value="#FFFFFF"/>
            <Setter Property="Foreground" Value="#24243A"/>
            <Setter Property="BorderBrush" Value="#D9DCE8"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="9,6"/>
            <Setter Property="Margin" Value="3"/>
            <Setter Property="MinHeight" Value="32"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
        </Style>
        <Style TargetType="{x:Type ComboBox}">
            <Setter Property="Background" Value="#FFFFFF"/>
            <Setter Property="Foreground" Value="#24243A"/>
            <Setter Property="BorderBrush" Value="#D9DCE8"/>
            <Setter Property="Padding" Value="7,5"/>
            <Setter Property="Margin" Value="3"/>
            <Setter Property="MinHeight" Value="32"/>
        </Style>
        <Style TargetType="{x:Type CheckBox}">
            <Setter Property="Foreground" Value="#55586D"/>
            <Setter Property="Margin" Value="8,4"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
        </Style>
        <Style TargetType="{x:Type TabControl}">
            <Setter Property="Background" Value="#F4F5FA"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="VerticalContentAlignment" Value="Stretch"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="{x:Type TabControl}">
                        <Grid ClipToBounds="True" SnapsToDevicePixels="True">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="184"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0" Orientation="Vertical" IsItemsHost="True" KeyboardNavigation.TabIndex="1"/>
                            <Border Grid.Column="1" Background="{TemplateBinding Background}"
                                    BorderBrush="#E1E3EC" BorderThickness="1" CornerRadius="10"
                                    Padding="6" MinWidth="0" MinHeight="0">
                                <ContentPresenter x:Name="PART_SelectedContentHost"
                                                  ContentSource="SelectedContent"
                                                  HorizontalAlignment="Stretch" VerticalAlignment="Stretch"
                                                  MinWidth="0" MinHeight="0"/>
                            </Border>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType="{x:Type TabItem}">
            <Setter Property="Width" Value="176"/>
            <Setter Property="Padding" Value="16,13"/>
            <Setter Property="Margin" Value="6,4"/>
            <Setter Property="HorizontalContentAlignment" Value="Left"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Foreground" Value="#686B80"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="{x:Type TabItem}">
                        <Border x:Name="TabBorder" Background="{TemplateBinding Background}" CornerRadius="9" Padding="{TemplateBinding Padding}">
                            <ContentPresenter ContentSource="Header" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="TabBorder" Property="Background" Value="#EEEAFE"/>
                                <Setter Property="Foreground" Value="#5B4AE8"/>
                                <Setter Property="FontWeight" Value="SemiBold"/>
                            </Trigger>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="TabBorder" Property="Background" Value="#EAEAF4"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType="{x:Type DataGrid}">
            <Setter Property="Background" Value="#FFFFFF"/>
            <Setter Property="Foreground" Value="#292A3E"/>
            <Setter Property="BorderBrush" Value="#E1E3EC"/>
            <Setter Property="RowBackground" Value="#FFFFFF"/>
            <Setter Property="AlternatingRowBackground" Value="#F8F8FC"/>
            <Setter Property="GridLinesVisibility" Value="Horizontal"/>
            <Setter Property="HorizontalGridLinesBrush" Value="#ECECF3"/>
            <Setter Property="HeadersVisibility" Value="Column"/>
            <Setter Property="RowHeight" Value="32"/>
            <Setter Property="ColumnHeaderHeight" Value="36"/>
            <Setter Property="Margin" Value="4"/>
        </Style>
        <Style TargetType="{x:Type ListView}">
            <Setter Property="Background" Value="#FFFFFF"/>
            <Setter Property="BorderBrush" Value="#E1E3EC"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Margin" Value="4"/>
        </Style>
        <Style TargetType="{x:Type ListBox}">
            <Setter Property="Background" Value="#FFFFFF"/>
            <Setter Property="BorderBrush" Value="#E1E3EC"/>
            <Setter Property="BorderThickness" Value="1"/>
        </Style>
'@

if (-not $xaml.Contains('</Window.Resources>')) { throw 'Window.Resources closing tag not found.' }
$xaml = [regex]::Replace($xaml, '(?s)[ \t]*<Style TargetType="Button">.*?</Style>\s*', '', 1)
$xaml = $xaml.Replace('</Window.Resources>', $theme + [Environment]::NewLine + '    </Window.Resources>')
$xaml = $xaml.Replace('<Grid Margin="8">','<Grid Margin="12" Background="#F4F5FA" HorizontalAlignment="Stretch" VerticalAlignment="Stretch">')
$xaml = $xaml.Replace('<WrapPanel Grid.Row="0">','<WrapPanel Grid.Row="0" Margin="4,4,4,12">')

# Rebuild the crowded connection bar as two aligned rows without changing control names or handlers.
$topBar = @'
        <Grid Grid.Row="0" Margin="4,4,4,10">
            <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
            <DockPanel Grid.Row="0" LastChildFill="False">
                <TextBlock DockPanel.Dock="Left" Text="SSH profile" VerticalAlignment="Center" Margin="0,0,8,0" FontWeight="SemiBold" Foreground="#55586D"/>
                <ComboBox x:Name="ProfileBox" Width="300" VerticalAlignment="Center" SelectionChanged="ProfileBox_SelectionChanged"/>
                <Button Content="Add" Click="AddProfile_Click"/>
                <Button Content="Edit" Click="EditProfile_Click"/>
                <Button Content="Delete" Click="DeleteProfile_Click"/>
                <Button x:Name="ConnectBtn" Content="Connect" FontWeight="SemiBold" Click="Connect_Click"/>
            </DockPanel>
            <WrapPanel Grid.Row="1" Margin="0,5,0,0">
                <TextBlock Text="Startup &amp; behaviour" VerticalAlignment="Center" Margin="0,0,8,0" FontWeight="SemiBold" Foreground="#74778B"/>
                <CheckBox x:Name="AutoConnectChk" Content="Auto-connect on start" VerticalAlignment="Center" Margin="8,0,8,0" Click="AutoConnectChk_Click"/>
                <CheckBox x:Name="AutoReconnectChk" Content="Auto-reconnect" VerticalAlignment="Center" Margin="8,0,8,0" Click="AutoReconnectChk_Click"/>
                <CheckBox x:Name="StartupChk" Content="Start with Windows" VerticalAlignment="Center" Margin="8,0,8,0" Click="StartupChk_Click"/>
                <CheckBox x:Name="StartMinChk" Content="Start minimized to tray" VerticalAlignment="Center" Margin="8,0,8,0" Click="StartMinChk_Click"/>
                <CheckBox x:Name="CloseTrayChk" Content="Close button hides to tray" VerticalAlignment="Center" Margin="8,0,8,0" Click="CloseTrayChk_Click"/>
            </WrapPanel>
        </Grid>
'@
# Replace the existing connection bar by locating its actual named controls.
# Do not replace an unrelated Grid.Row=0 WrapPanel (which caused duplicate x:Name fields).
$topBarPattern = '(?s)<WrapPanel\\b[^>]*>.*?</WrapPanel>'
$topBarMatches = [regex]::Matches($xaml, $topBarPattern)
$topBarMatch = $null
foreach ($candidate in $topBarMatches) {
    $candidateText = $candidate.Value
    if ($candidateText -match 'x:Name="ProfileBox"' -and
        $candidateText -match 'x:Name="ConnectBtn"' -and
        $candidateText -match 'x:Name="AutoConnectChk"' -and
        $candidateText -match 'x:Name="AutoReconnectChk"' -and
        $candidateText -match 'x:Name="StartupChk"' -and
        $candidateText -match 'x:Name="StartMinChk"' -and
        $candidateText -match 'x:Name="CloseTrayChk"') {
        $topBarMatch = $candidate
        break
    }
}
if ($null -eq $topBarMatch) {
    throw 'Could not uniquely locate the original SSH connection bar containing all seven named controls. Refusing to insert duplicate WPF controls.'
}
$xaml = $xaml.Substring(0, $topBarMatch.Index) + $topBar.Trim() + $xaml.Substring($topBarMatch.Index + $topBarMatch.Length)

$xaml = $xaml.Replace('Margin="0,6,0,6" SelectionChanged="MainTabs_SelectionChanged"','Margin="0,8,0,8" SelectionChanged="MainTabs_SelectionChanged"')

Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8

# 4. Ensure ConsoleClear_Click exists in MainWindow.xaml.cs without clobbering Apply-Persistent-Shell.ps1
$code = Get-Content $codePath -Raw
if ($code -notmatch 'ConsoleClear_Click') {
    $clearMethod = @'
    private void ConsoleClear_Click(object sender, RoutedEventArgs e)
    {
        ConsoleOut.Clear();
        ConsoleOut.ScrollToHome();
    }

'@
    $lastBrace = $code.LastIndexOf('}')
    if ($lastBrace -lt 0) { throw 'MainWindow class closing brace not found.' }
    $code = $code.Insert($lastBrace, $clearMethod)
    Set-Content -Path $codePath -Value $code -Encoding UTF8
}

Write-Host 'Console layout, stretched output boxes (ConsoleOut, ScriptOut, LogBox, AdbOut, AppOut, HistoryOut), and ConsoleClear_Click applied.'