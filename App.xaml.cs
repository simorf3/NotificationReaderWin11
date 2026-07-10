<Window x:Class="NotificationReader.Windows.FilterRuleDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Filter Rule"
        Height="420" Width="480"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False"
        ResizeMode="NoResize"
        SizeToContent="Manual">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="110"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>

        <TextBlock Grid.Row="0" Grid.Column="0" Text="Name:" VerticalAlignment="Center" Margin="0,0,0,8"/>
        <TextBox   Grid.Row="0" Grid.Column="1" Grid.ColumnSpan="2" x:Name="NameBox" Margin="0,0,0,8"/>

        <TextBlock Grid.Row="1" Grid.Column="0" Text="Pattern (regex):" VerticalAlignment="Center" Margin="0,0,0,8"/>
        <TextBox   Grid.Row="1" Grid.Column="1" x:Name="PatternBox" Margin="0,0,8,8"/>
        <Button    Grid.Row="1" Grid.Column="2" Content="Test" Width="60" Margin="0,0,0,8" Click="Test_Click"/>

        <TextBlock Grid.Row="2" Grid.Column="0" Text="Target:" VerticalAlignment="Center" Margin="0,0,0,8"/>
        <ComboBox  Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" x:Name="TargetBox" Margin="0,0,0,8"/>

        <TextBlock Grid.Row="3" Grid.Column="0" Text="Action:" VerticalAlignment="Center" Margin="0,0,0,8"/>
        <ComboBox  Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2" x:Name="ActionBox" Margin="0,0,0,8"/>

        <CheckBox  Grid.Row="4" Grid.Column="1" Grid.ColumnSpan="2" x:Name="EnabledBox"
                   Content="Enabled" IsChecked="True" Margin="0,0,0,12"/>

        <TextBlock Grid.Row="5" Grid.Column="0" Grid.ColumnSpan="3"
                   Text="Test against sample text:" Margin="0,4,0,4"/>
        <TextBox   Grid.Row="6" Grid.Column="0" Grid.ColumnSpan="3" x:Name="SampleBox"
                   Height="60" TextWrapping="Wrap" AcceptsReturn="True"
                   VerticalScrollBarVisibility="Auto" Margin="0,0,0,8"/>

        <TextBlock Grid.Row="7" Grid.Column="0" Grid.ColumnSpan="3" x:Name="TestResultText"
                   Margin="0,0,0,8" FontWeight="Bold"/>

        <StackPanel Grid.Row="9" Grid.Column="0" Grid.ColumnSpan="3"
                    Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="OK" Width="80" Margin="4,0" IsDefault="True" Click="Ok_Click"/>
            <Button Content="Cancel" Width="80" Margin="4,0" IsCancel="True" Click="Cancel_Click"/>
        </StackPanel>
    </Grid>
</Window>
