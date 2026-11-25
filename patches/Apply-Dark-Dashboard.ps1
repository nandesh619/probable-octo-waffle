$ErrorActionPreference = 'Stop'
$xamlPath = 'SshAdbHub/MainWindow.xaml'
$codePath = 'SshAdbHub/MainWindow.xaml.cs'
$xaml = Get-Content $xamlPath -Raw

# Add the Dashboard home tab with responsive full-width cards, readable text, and XML entity icons (encoding-safe).
if ($xaml -notmatch 'x:Name="HomeTab"') {
$homeXaml = @'
            <TabItem x:Name="HomeTab" Header="&#x2302;  Dashboard">
              <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                <Grid Margin="14" HorizontalAlignment="Stretch">
                  <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                  </Grid.RowDefinitions>

                  <TextBlock Grid.Row="0" Text="Control centre" FontSize="26" FontWeight="Bold" Foreground="#24243A"/>
                  <TextBlock Grid.Row="1" Text="Your router, Android devices and remote tools in one place." Margin="0,4,0,18" Foreground="#667085"/>

                  <Grid Grid.Row="2" Margin="0,0,0,20">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="12"/>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="12"/>
                      <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Border Grid.Column="0" Background="#F8F9FC" BorderBrush="#E1E3EC" BorderThickness="1" CornerRadius="10" Padding="16">
                      <StackPanel>
                        <TextBlock Text="SSH CONNECTION" Foreground="#667085" FontSize="10.5" FontWeight="Bold"/>
                        <TextBlock Text="Router access" Foreground="#24243A" FontSize="18" FontWeight="SemiBold" Margin="0,6,0,4"/>
                        <TextBlock Text="Connect using your saved OpenWrt profile" Foreground="#55586D" TextWrapping="Wrap"/>
                      </StackPanel>
                    </Border>
                    <Border Grid.Column="2" Background="#F8F9FC" BorderBrush="#E1E3EC" BorderThickness="1" CornerRadius="10" Padding="16">
                      <StackPanel>
                        <TextBlock Text="SFTP FILES" Foreground="#667085" FontSize="10.5" FontWeight="Bold"/>
                        <TextBlock Text="Remote storage" Foreground="#24243A" FontSize="18" FontWeight="SemiBold" Margin="0,6,0,4"/>
                        <TextBlock Text="Browse, upload and download router files" Foreground="#55586D" TextWrapping="Wrap"/>
                      </StackPanel>
                    </Border>
                    <Border Grid.Column="4" Background="#F8F9FC" BorderBrush="#E1E3EC" BorderThickness="1" CornerRadius="10" Padding="16">
                      <StackPanel>
                        <TextBlock Text="ADB DEVICES" Foreground="#667085" FontSize="10.5" FontWeight="Bold"/>
                        <TextBlock Text="Android control" Foreground="#24243A" FontSize="18" FontWeight="SemiBold" Margin="0,6,0,4"/>
                        <TextBlock Text="Connect, inspect and manage Android devices" Foreground="#55586D" TextWrapping="Wrap"/>
                      </StackPanel>
                    </Border>
                  </Grid>

                  <TextBlock Grid.Row="3" Text="QUICK ACTIONS" FontSize="11" FontWeight="Bold" Foreground="#667085" Margin="0,0,0,8"/>

                  <UniformGrid Grid.Row="4" Columns="4" Rows="2" HorizontalAlignment="Stretch">
                    <Button Content="&#x2328;  Open Console" Tag="2" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0,0,8,8"/>
                    <Button Content="&#x25A4;  Browse Files" Tag="1" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0,0,8,8"/>
                    <Button Content="&#x25A3;  ADB Devices" Tag="3" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0,0,8,8"/>
                    <Button Content="&#x25C9;  Router Dashboard" Tag="5" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0,0,0,8"/>
                    <Button Content="&#x25C7;  Manage Apps" Tag="6" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0,0,8,0"/>
                    <Button Content="&#x25B6;  Run Scripts" Tag="4" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0,0,8,0"/>
                    <Button Content="&#x25F7;  Run History" Tag="7" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0,0,8,0"/>
                    <Button Content="&#x2261;  Application Logs" Tag="8" Click="HomeNavigate_Click" MinHeight="62" HorizontalAlignment="Stretch" Margin="0"/>
                  </UniformGrid>

                  <Border Grid.Row="5" Background="#F8F9FC" BorderBrush="#E1E3EC" BorderThickness="1" CornerRadius="10" Padding="16" Margin="0,20,0,0">
                    <StackPanel>
                      <TextBlock Text="GET STARTED" Foreground="#5B4AE8" FontWeight="Bold" FontSize="10.5"/>
                      <TextBlock Text="Choose an SSH profile in the top bar, then connect. SFTP, the terminal and router dashboard all use that connection." Foreground="#3E4357" TextWrapping="Wrap" Margin="0,6,0,0"/>
                    </StackPanel>
                  </Border>
                </Grid>
              </ScrollViewer>
            </TabItem>
'@
# Insert HomeTab AFTER any TabControl.Resources block: property elements must precede child items.
$tabRegex = [regex]'(?s)<TabControl x:Name="MainTabs"[^>]*>(?:\s*<TabControl\.Resources>.*?</TabControl\.Resources>)?'
$tabMatch = $tabRegex.Match($xaml)
if (-not $tabMatch.Success) { throw 'MainTabs TabControl not found in MainWindow.xaml.' }
$xaml = $xaml.Substring(0, $tabMatch.Index + $tabMatch.Length) + [Environment]::NewLine + $homeXaml + $xaml.Substring($tabMatch.Index + $tabMatch.Length)
Set-Content $xamlPath $xaml -Encoding UTF8
}

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
Write-Host 'Applied responsive light Dashboard home tab and navigation.'
