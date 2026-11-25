$ErrorActionPreference = 'Stop'
$xamlPath = 'SshAdbHub/MainWindow.xaml'
$codePath = 'SshAdbHub/MainWindow.xaml.cs'
$xaml = Get-Content $xamlPath -Raw

# Dark control-centre palette. Existing controls and event handlers remain intact.
# Keep the approved dashboard layout but use a conventional light Windows palette.
$palette = @{}
foreach ($key in $palette.Keys) { $xaml = $xaml.Replace($key, $palette[$key]) }
$xaml = [regex]::Replace($xaml, 'Title="SSH \+ ADB Hub" Height="[^"]+" Width="[^"]+" MinHeight="[^"]+" MinWidth="[^"]+"', 'Title="SSH + ADB Hub" Height="900" Width="1440" MinHeight="700" MinWidth="1080"', 1)
# Update only the actual root Window element. Do not match Window.Resources.
$rootPattern = '(?m)^\s*<Window(?=\s|>)[^>]*>'
$rootMatch = [regex]::Match($xaml, $rootPattern)
if (-not $rootMatch.Success) { throw 'Could not locate the root Window element in MainWindow.xaml.' }
$windowTag = $rootMatch.Value
foreach ($attribute in @{ Background = '#0A1020'; Foreground = '#EEF3FF' }.GetEnumerator()) {
    $attributePattern = '(?<![\w:.-])' + [regex]::Escape($attribute.Key) + '="[^"]*"'
    if ([regex]::IsMatch($windowTag, $attributePattern)) {
        $windowTag = [regex]::Replace($windowTag, $attributePattern, ($attribute.Key + '="' + $attribute.Value + '"'), 1)
    } else {
        $windowTag = $windowTag.Substring(0, $windowTag.Length - 1) + ' ' + $attribute.Key + '="' + $attribute.Value + '">'
    }
}
$xaml = $xaml.Substring(0, $rootMatch.Index) + $windowTag + $xaml.Substring($rootMatch.Index + $rootMatch.Length)
$xaml = $xaml.Replace('<Grid Margin="8">','<Grid Margin="14" Background="#0A1020">')
$xaml = $xaml.Replace('<WrapPanel Grid.Row="0">','<WrapPanel Grid.Row="0" Margin="4,4,4,12">')

# Add a useful home page; shortcuts select the existing functional pages.
if ($xaml -notmatch 'x:Name="HomeTab"') {
$homeXaml = @'
            <TabItem x:Name="HomeTab" Header="⌂   Dashboard">
              <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel Margin="26">
                  <TextBlock Text="Control centre" FontSize="28" FontWeight="Bold" Foreground="#EEF3FF"/>
                  <TextBlock Text="Your router, Android devices and remote tools in one place." Margin="0,5,0,22" Foreground="#9BAAC5"/>
                  <UniformGrid Columns="3" Margin="0,0,0,20">
                    <Border Background="#14233B" BorderBrush="#2B3A56" BorderThickness="1" CornerRadius="12" Padding="18" Margin="0,0,10,0"><StackPanel><TextBlock Text="SSH CONNECTION" Foreground="#9BAAC5" FontSize="10" FontWeight="Bold"/><TextBlock Text="Router access" Foreground="#EEF3FF" FontSize="19" FontWeight="SemiBold" Margin="0,8,0,4"/><TextBlock Text="Connect using your saved OpenWrt profile" Foreground="#9BAAC5" TextWrapping="Wrap"/></StackPanel></Border>
                    <Border Background="#14233B" BorderBrush="#2B3A56" BorderThickness="1" CornerRadius="12" Padding="18" Margin="5,0"><StackPanel><TextBlock Text="SFTP FILES" Foreground="#9BAAC5" FontSize="10" FontWeight="Bold"/><TextBlock Text="Remote storage" Foreground="#EEF3FF" FontSize="19" FontWeight="SemiBold" Margin="0,8,0,4"/><TextBlock Text="Browse, upload and download router files" Foreground="#9BAAC5" TextWrapping="Wrap"/></StackPanel></Border>
                    <Border Background="#14233B" BorderBrush="#2B3A56" BorderThickness="1" CornerRadius="12" Padding="18" Margin="10,0,0,0"><StackPanel><TextBlock Text="ADB DEVICES" Foreground="#9BAAC5" FontSize="10" FontWeight="Bold"/><TextBlock Text="Android control" Foreground="#EEF3FF" FontSize="19" FontWeight="SemiBold" Margin="0,8,0,4"/><TextBlock Text="Connect, inspect and manage Android devices" Foreground="#9BAAC5" TextWrapping="Wrap"/></StackPanel></Border>
                  </UniformGrid>
                  <TextBlock Text="QUICK ACTIONS" FontSize="11" FontWeight="Bold" Foreground="#9BAAC5" Margin="0,0,0,10"/>
                  <UniformGrid Columns="4" Rows="2">
                    <Button Content="⌨  Open Console" Tag="2" Click="HomeNavigate_Click" MinHeight="64" Margin="0,0,8,8"/>
                    <Button Content="▤  Browse Files" Tag="1" Click="HomeNavigate_Click" MinHeight="64" Margin="0,0,8,8"/>
                    <Button Content="▣  ADB Devices" Tag="3" Click="HomeNavigate_Click" MinHeight="64" Margin="0,0,8,8"/>
                    <Button Content="◉  Router Dashboard" Tag="5" Click="HomeNavigate_Click" MinHeight="64" Margin="0,0,0,8"/>
                    <Button Content="◇  Manage Apps" Tag="6" Click="HomeNavigate_Click" MinHeight="64" Margin="0,0,8,0"/>
                    <Button Content="▶  Run Scripts" Tag="4" Click="HomeNavigate_Click" MinHeight="64" Margin="0,0,8,0"/>
                    <Button Content="◷  Run History" Tag="7" Click="HomeNavigate_Click" MinHeight="64" Margin="0,0,8,0"/>
                    <Button Content="≡  Application Logs" Tag="8" Click="HomeNavigate_Click" MinHeight="64"/>
                  </UniformGrid>
                  <Border Background="#121C30" BorderBrush="#2B3A56" BorderThickness="1" CornerRadius="12" Padding="16" Margin="0,20,0,0"><StackPanel><TextBlock Text="GET STARTED" Foreground="#A99FFF" FontWeight="Bold" FontSize="10"/><TextBlock Text="Choose an SSH profile in the top bar, then connect. SFTP, the terminal and router dashboard all use that connection." Foreground="#C7D3E8" TextWrapping="Wrap" Margin="0,7,0,0"/></StackPanel></Border>
                </StackPanel>
              </ScrollViewer>
            </TabItem>
'@
# Insert HomeTab AFTER any TabControl.Resources block: property elements must precede child items.
$tabRegex = [regex]'(?s)<TabControl x:Name="MainTabs"[^>]*>(?:\s*<TabControl\.Resources>.*?</TabControl\.Resources>)?'
$tabMatch = $tabRegex.Match($xaml)
if (-not $tabMatch.Success) { throw 'MainTabs TabControl not found in MainWindow.xaml.' }
$xaml = $xaml.Substring(0, $tabMatch.Index + $tabMatch.Length) + [Environment]::NewLine + $homeXaml + $xaml.Substring($tabMatch.Index + $tabMatch.Length)
}
$styles = @'
        <Style TargetType="{x:Type Button}">
          <Setter Property="Background" Value="#1A2943"/><Setter Property="Foreground" Value="#E8EEFC"/><Setter Property="BorderBrush" Value="#344766"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="13,8"/><Setter Property="Margin" Value="4"/><Setter Property="MinHeight" Value="34"/><Setter Property="FontSize" Value="12.5"/><Setter Property="FontWeight" Value="SemiBold"/><Setter Property="Cursor" Value="Hand"/>
          <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#293B5D"/><Setter Property="BorderBrush" Value="#8D83FF"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter Property="Background" Value="#354A72"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></Style.Triggers>
        </Style>
        <Style TargetType="{x:Type TextBox}"><Setter Property="Background" Value="#0E192B"/><Setter Property="Foreground" Value="#EAF0FF"/><Setter Property="BorderBrush" Value="#30415F"/><Setter Property="Padding" Value="9,6"/><Setter Property="Margin" Value="3"/><Setter Property="MinHeight" Value="32"/></Style>
        <Style TargetType="{x:Type ComboBox}"><Setter Property="Background" Value="#15233A"/><Setter Property="Foreground" Value="#EAF0FF"/><Setter Property="BorderBrush" Value="#30415F"/><Setter Property="Padding" Value="7,5"/><Setter Property="Margin" Value="3"/></Style>
        <Style TargetType="{x:Type CheckBox}"><Setter Property="Foreground" Value="#B8C5DD"/><Setter Property="Margin" Value="8,4"/></Style>
        <Style TargetType="{x:Type TabControl}"><Setter Property="Background" Value="#0A1020"/><Setter Property="BorderThickness" Value="0"/></Style>
        <Style TargetType="{x:Type TabItem}"><Setter Property="Width" Value="196"/><Setter Property="Padding" Value="15,13"/><Setter Property="Margin" Value="5,4"/><Setter Property="HorizontalContentAlignment" Value="Left"/><Setter Property="FontSize" Value="13"/><Setter Property="Foreground" Value="#9BAAC5"/><Setter Property="Background" Value="Transparent"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type TabItem}"><Border x:Name="NavSurface" Background="{TemplateBinding Background}" CornerRadius="9" Padding="{TemplateBinding Padding}"><ContentPresenter ContentSource="Header" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="NavSurface" Property="Background" Value="#263554"/><Setter Property="Foreground" Value="#B8AEFF"/><Setter Property="FontWeight" Value="SemiBold"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="NavSurface" Property="Background" Value="#1A2943"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        <Style TargetType="{x:Type DataGrid}"><Setter Property="Background" Value="#111C2F"/><Setter Property="Foreground" Value="#EAF0FF"/><Setter Property="BorderBrush" Value="#2B3A56"/><Setter Property="RowBackground" Value="#121F34"/><Setter Property="AlternatingRowBackground" Value="#16243A"/><Setter Property="GridLinesVisibility" Value="Horizontal"/><Setter Property="HorizontalGridLinesBrush" Value="#263650"/><Setter Property="RowHeight" Value="34"/><Setter Property="ColumnHeaderHeight" Value="38"/><Setter Property="Margin" Value="4"/></Style>
        <Style TargetType="{x:Type ListView}"><Setter Property="Background" Value="#111C2F"/><Setter Property="Foreground" Value="#EAF0FF"/><Setter Property="BorderBrush" Value="#2B3A56"/><Setter Property="Margin" Value="4"/></Style>
        <Style TargetType="{x:Type ListBox}"><Setter Property="Background" Value="#111C2F"/><Setter Property="Foreground" Value="#EAF0FF"/><Setter Property="BorderBrush" Value="#2B3A56"/></Style>
'@
if ($xaml -notmatch 'TargetType="{x:Type Button}"') {
    if ($xaml -match '</Window.Resources>') {
        $xaml = $xaml.Replace('</Window.Resources>', $styles + [Environment]::NewLine + '</Window.Resources>')
    } else {
        $xaml = [regex]::Replace($xaml, '(<Window\b[^>]*>)', ('$1' + [Environment]::NewLine + '<Window.Resources>' + [Environment]::NewLine + $styles + [Environment]::NewLine + '</Window.Resources>'), 1)
    }
}
# Convert the dashboard's former dark-only literals to the same light palette used by every other page.
$lightPalette = @{
 '#0A1020'='#F4F5FA'; '#121C30'='#FFFFFF'; '#EEF3FF'='#24243A'; '#20243A'='#24243A'
 '#9BAAC5'='#74778B'; '#14233B'='#FFFFFF'; '#2B3A56'='#E1E3EC'; '#263554'='#EEEAFE'
 '#E8EEFC'='#45416F'; '#1A2943'='#FFFFFF'; '#344766'='#DDD9F3'; '#293B5D'='#EEEAFE'
 '#8D83FF'='#BDB5F0'; '#354A72'='#DED8FC'; '#0E192B'='#FFFFFF'; '#EAF0FF'='#24243A'
 '#30415F'='#D9DCE8'; '#15233A'='#FFFFFF'; '#B8C5DD'='#55586D'; '#111C2F'='#FFFFFF'
 '#121F34'='#FFFFFF'; '#16243A'='#F8F8FC'; '#263650'='#ECECF3'; '#A99FFF'='#5B4AE8'
}
foreach ($key in $lightPalette.Keys) { $xaml = $xaml.Replace($key, $lightPalette[$key]) }
Set-Content $xamlPath $xaml -Encoding UTF8
$code = Get-Content $codePath -Raw
if ($code -notmatch 'HomeNavigate_Click') {
$handler = @'
    private void HomeNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var index) && index >= 0 && index < MainTabs.Items.Count)
            MainTabs.SelectedIndex = index;
    }

'@
$last = $code.LastIndexOf('}')
if ($last -lt 0) { throw 'MainWindow class closing brace not found.' }
$code = $code.Insert($last, $handler)
Set-Content $codePath $code -Encoding UTF8
}
Write-Host 'Applied dark control-centre dashboard, navigation rail and working quick actions.'
