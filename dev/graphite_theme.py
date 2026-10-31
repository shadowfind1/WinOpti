# Разово: перевод Theme.xaml на дизайн «Графит» (шрифты, кнопки, меню, переключатели).
import io

p = r'UI/Theme.xaml'
s = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in s else '\n'


def rep(a, b):
    global s
    a = a.replace('\n', nl)
    b = b.replace('\n', nl)
    assert a in s, a
    s = s.replace(a, b, 1)


rep('''    <!-- Палитра: тёмная, фирменный градиент логотипа (фиолетовый → синий) как акцент.''',
    '''    <!-- Палитра «Графит»: почти чёрный фон, один яркий акцент — салатовый, цифры моноширинным шрифтом.''')
rep('''    <SolidColorBrush x:Key="Accent" Color="#C6FF3D"/>''',
    '''    <SolidColorBrush x:Key="Accent" Color="#C6FF3D"/>
    <!-- текст и значки поверх акцента: салатовый светлый, поэтому на нём тёмное -->
    <SolidColorBrush x:Key="OnAccent" Color="#08090B"/>''')
rep('''    <LinearGradientBrush x:Key="AccentGradient" StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="#8B5CFF" Offset="0"/>
        <GradientStop Color="#6EA8FE" Offset="1"/>
    </LinearGradientBrush>''',
    '''    <!-- раньше был градиент; в «Графите» акцент сплошной, ключ оставлен для совместимости -->
    <SolidColorBrush x:Key="AccentGradient" Color="#C6FF3D"/>''')
rep('''    <FontFamily x:Key="UiFont">Segoe UI Variable Text, Segoe UI</FontFamily>
    <FontFamily x:Key="DisplayFont">Segoe UI Variable Display, Segoe UI</FontFamily>''',
    r'''    <!-- шрифты зашиты в программу (Assets\Fonts, лицензия OFL): на любом ПК выглядят одинаково -->
    <FontFamily x:Key="UiFont">pack://application:,,,/Assets/Fonts/#Inter Tight, Segoe UI</FontFamily>
    <FontFamily x:Key="DisplayFont">pack://application:,,,/Assets/Fonts/#Inter Tight, Segoe UI</FontFamily>
    <FontFamily x:Key="NumFont">pack://application:,,,/Assets/Fonts/#JetBrains Mono, Consolas</FontFamily>''')
# акцентная кнопка: тёмный текст, строже радиус
rep('''        <Setter Property="Foreground" Value="White"/>
        <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Padding" Value="20,10"/>''',
    '''        <Setter Property="Foreground" Value="{StaticResource OnAccent}"/>
        <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="FontWeight" Value="Bold"/>
        <Setter Property="Padding" Value="20,11"/>''')
rep('''                        <Border x:Name="bg" CornerRadius="10" Background="{StaticResource AccentGradient}"/>
                        <Border x:Name="hi" CornerRadius="10" Background="White" Opacity="0"/>''',
    '''                        <Border x:Name="bg" CornerRadius="8" Background="{StaticResource Accent}"/>
                        <Border x:Name="hi" CornerRadius="8" Background="White" Opacity="0"/>''')
# обычная кнопка: прозрачная с рамкой
rep('''                    <Border x:Name="bg" CornerRadius="9" Background="{StaticResource CardAlt}" BorderBrush="{StaticResource Line}" BorderThickness="1">''',
    '''                    <Border x:Name="bg" CornerRadius="8" Background="Transparent" BorderBrush="#2A2E35" BorderThickness="1">''')
rep('''                            <Setter TargetName="bg" Property="Background" Value="#1A1D22"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="bg" Property="Opacity" Value="0.4"/>''',
    '''                            <Setter TargetName="bg" Property="Background" Value="#14171B"/>
                            <Setter TargetName="bg" Property="BorderBrush" Value="#3A3F47"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="bg" Property="Opacity" Value="0.4"/>''')
# меню: тонкая салатовая полоска, радиус 8
rep('''                        <Border x:Name="bg" CornerRadius="10" Background="Transparent"/>
                        <Border x:Name="bar" Width="3" Height="18" CornerRadius="2" HorizontalAlignment="Left"
                                Background="{StaticResource AccentGradient}" Opacity="0"/>''',
    '''                        <Border x:Name="bg" CornerRadius="8" Background="Transparent"/>
                        <Border x:Name="bar" Width="2" Height="22" CornerRadius="1" HorizontalAlignment="Left"
                                Background="{StaticResource Accent}" Opacity="0"/>''')
# переключатель и галочка
rep('''                        <Border x:Name="on" CornerRadius="13" Background="{StaticResource AccentGradient}" Opacity="0"/>
                        <Ellipse x:Name="knob" Width="18" Height="18" Fill="White" HorizontalAlignment="Left" Margin="4,0,0,0">''',
    '''                        <Border x:Name="on" CornerRadius="13" Background="{StaticResource Accent}" Opacity="0"/>
                        <Ellipse x:Name="knob" Width="18" Height="18" Fill="#E8EAED" HorizontalAlignment="Left" Margin="4,0,0,0">''')
rep('''                        <Border x:Name="box" CornerRadius="7" BorderThickness="1.5" BorderBrush="#3A3F47" Background="Transparent"/>
                        <Border x:Name="fill" CornerRadius="7" Background="{StaticResource AccentGradient}" Opacity="0"/>
                        <TextBlock x:Name="mark" Text="&#xE73E;" FontFamily="{StaticResource Icons}" FontSize="11" Foreground="White"''',
    '''                        <Border x:Name="box" CornerRadius="5" BorderThickness="1.5" BorderBrush="#3A3F47" Background="Transparent"/>
                        <Border x:Name="fill" CornerRadius="5" Background="{StaticResource Accent}" Opacity="0"/>
                        <TextBlock x:Name="mark" Text="&#xE73E;" FontFamily="{StaticResource Icons}" FontSize="11" FontWeight="Bold" Foreground="{StaticResource OnAccent}"''')
rep('''                    <Border x:Name="bg" CornerRadius="9" Padding="14,7" Background="Transparent">''',
    '''                    <Border x:Name="bg" CornerRadius="6" Padding="14,7" Background="Transparent">''')
rep('''<Border x:Name="bg" CornerRadius="10" Background="{StaticResource Card}" BorderBrush="{StaticResource Line}" BorderThickness="1" Padding="12,0">''',
    '''<Border x:Name="bg" CornerRadius="8" Background="{StaticResource Card}" BorderBrush="{StaticResource Line}" BorderThickness="1" Padding="12,0">''')

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
