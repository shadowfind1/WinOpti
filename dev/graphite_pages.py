# Разово: страницы и код под дизайн «Графит» — текст на акценте тёмный, строже скругления, цифры моноширинные.
import glob
import io
import re


def load(p):
    return io.open(p, encoding='utf-8', newline='').read()


def save(p, s):
    io.open(p, 'w', encoding='utf-8', newline='').write(s)


def edit(p, pairs):
    s = load(p)
    nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl)
        b = b.replace('\n', nl)
        assert a in s, (p, a)
        s = s.replace(a, b, 1)
    save(p, s)


# 1. на салатовом — тёмный текст; скругления строже
for p in glob.glob('UI/*.xaml') + ['MainWindow.xaml']:
    if p.endswith('Theme.xaml'):
        continue
    s = load(p)
    o = s
    s = s.replace('Foreground="White"', 'Foreground="{StaticResource OnAccent}"')
    s = s.replace('CornerRadius="18"', 'CornerRadius="12"').replace('CornerRadius="14"', 'CornerRadius="10"')
    if s != o:
        save(p, s)
        print('restyled', p)

# 2. логотип и значок версии в меню
edit('MainWindow.xaml', [(
    '''                    <Image Source="/Assets/logo.png" Width="34" Height="34" RenderOptions.BitmapScalingMode="HighQuality"/>''',
    '''                    <Border Width="32" Height="32" CornerRadius="8" Background="{StaticResource Accent}" VerticalAlignment="Center">
                        <Path Data="M5,12.5 L12,5.5 L19,12.5 M5,18.5 L12,11.5 L19,18.5" Stroke="{StaticResource OnAccent}" StrokeThickness="2.9"
                              StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" Width="24" Height="24" Stretch="None"
                              HorizontalAlignment="Center" VerticalAlignment="Center" RenderTransformOrigin="0.5,0.5">
                            <Path.RenderTransform><ScaleTransform ScaleX="0.75" ScaleY="0.75"/></Path.RenderTransform>
                        </Path>
                    </Border>'''), (
    '''                    <Border Background="{StaticResource AccentGradient}" CornerRadius="6" Padding="7,1" VerticalAlignment="Center">
                        <TextBlock Text="2.0" FontSize="11" FontWeight="Bold" Foreground="{StaticResource OnAccent}"/>
                    </Border>''',
    '''                    <Border BorderBrush="#2A2E35" BorderThickness="1" CornerRadius="4" Padding="5,1" VerticalAlignment="Center">
                        <TextBlock Text="2.0" FontFamily="{StaticResource NumFont}" FontSize="11" Foreground="{StaticResource Muted}"/>
                    </Border>'''),
])

# 3. крупные цифры — моноширинным шрифтом
edit('UI/CleanupPage.xaml', [(
    '<TextBlock x:Name="Total" Text="…" FontFamily="{StaticResource DisplayFont}"',
    '<TextBlock x:Name="Total" Text="…" FontFamily="{StaticResource NumFont}"')])
edit('UI/FpsPage.xaml', [
    ('<TextBlock x:Name="Timer" Text="10" FontFamily="{StaticResource DisplayFont}"', '<TextBlock x:Name="Timer" Text="10" FontFamily="{StaticResource NumFont}"'),
    ('<TextBlock Text="{Binding Avg}" FontSize="17" FontWeight="SemiBold"/>', '<TextBlock Text="{Binding Avg}" FontFamily="{StaticResource NumFont}" FontSize="17" FontWeight="Bold"/>'),
    ('<TextBlock Text="{Binding Low1}" FontSize="17" FontWeight="SemiBold"/>', '<TextBlock Text="{Binding Low1}" FontFamily="{StaticResource NumFont}" FontSize="17" FontWeight="Bold"/>'),
    ('<TextBlock Text="{Binding Stutters}" FontSize="17" FontWeight="SemiBold"/>', '<TextBlock Text="{Binding Stutters}" FontFamily="{StaticResource NumFont}" FontSize="17" FontWeight="Bold"/>'),
    ('<TextBlock Text="{Binding Label}" FontSize="11" FontWeight="SemiBold" Foreground="{StaticResource OnAccent}"/>',
     '<TextBlock Text="{Binding Label}" FontSize="11" FontWeight="Bold" Foreground="{Binding LabelForeground}"/>'),
])
edit('UI/ChecksPage.xaml', [(
    '<TextBlock x:Name="MouseHz" FontFamily="{StaticResource DisplayFont}"', '<TextBlock x:Name="MouseHz" FontFamily="{StaticResource NumFont}"')])
edit('UI/FpsPage.xaml.cs', [
    ('''        public Brush LabelBrush => Run.Label == "После" ? (Brush)Application.Current.FindResource("AccentGradient") : new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x55));''',
     '''        public Brush LabelBrush => (Brush)Application.Current.FindResource(Run.Label == "После" ? "Accent" : "CardAlt");
        public Brush LabelForeground => (Brush)Application.Current.FindResource(Run.Label == "После" ? "OnAccent" : "Text");'''),
    ('''            var nums = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };''',
     '''            var nums = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)FindResource("NumFont"), Margin = new Thickness(0, 4, 0, 0) };'''),
])
edit('UI/ChecksPage.xaml.cs', [(
    'GameModeButtonText.Foreground = _gameModeOn ? (Brush)FindResource("Text") : Brushes.White;',
    'GameModeButtonText.Foreground = (Brush)FindResource(_gameModeOn ? "Text" : "OnAccent");')])

# 4. кольцо оценки: цифра моноширинная, дорожка и подпись — в цвет темы
edit('UI/ScoreRing.cs', [
    ('Color.FromRgb(0x1E, 0x24, 0x33)', 'Color.FromRgb(0x1C, 0x20, 0x26)'),
    ('new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)',
     'new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)'),
    ('new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF6))', 'new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED))'),
    ('new Typeface("Segoe UI"), size * 0.085, new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xAD))',
     'new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter Tight"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.085, new SolidColorBrush(Color.FromRgb(0x8B, 0x90, 0x99))'),
])

# 5. карточка результата: чёрный фон, салатовое свечение вместо фиолетового
edit('UI/ShareCard.cs', [
    ('new LinearGradientBrush(Color.FromRgb(0x10, 0x12, 0x1C), Color.FromRgb(0x07, 0x08, 0x0C), 90)',
     'new LinearGradientBrush(Color.FromRgb(0x0D, 0x0F, 0x12), Color.FromRgb(0x06, 0x07, 0x08), 90)'),
    ('Color.FromArgb(0x70, 0x7C, 0x5C, 0xFF)', 'Color.FromArgb(0x38, 0xC6, 0xFF, 0x3D)'),
])

# 6. шрифты — в сборку
edit('WinOpti.csproj', [(
    '    <Resource Include="Assets\\logo.png" />',
    '    <Resource Include="Assets\\logo.png" />\n    <Resource Include="Assets\\Fonts\\*.ttf" />')])
print('ok')
