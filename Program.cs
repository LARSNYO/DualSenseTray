using System.Drawing;
using System.Runtime.InteropServices;
using DualSenseAPI;
using DualSenseAPI.State;
using Timer = System.Windows.Forms.Timer;

namespace DualSenseTray;

internal static class Program
{
       private static NotifyIcon? _notifyIcon;
       private static Timer? _connectionTimer;

       private static DualSense? _gamePad;

       // Последние полученные данные
       private static int _batteryLevel = 0;
       private static bool _isCharging = false;

       // Защита от одновременного подключения/отключения
       private static readonly object _gamePadLock = new();

       [STAThread]
       private static void Main()
       {
              ApplicationConfiguration.Initialize();

              // =========================
              // Контекстное меню
              // =========================

              var contextMenu = new ContextMenuStrip();

              contextMenu.Items.Add(
                  "Exit",
                  null,
                  (_, _) => ExitApplication()
              );

              // =========================
              // Иконка в трее
              // =========================

              _notifyIcon = new NotifyIcon
              {
                     Icon = SystemIcons.Application,
                     ContextMenuStrip = contextMenu,
                     Visible = true,
                     Text = "DualSense: Searching..."
              };

              // =========================
              // Таймер проверки подключения
              // =========================

              _connectionTimer = new Timer
              {
                     Interval = 3000
              };

              _connectionTimer.Tick += (_, _) =>
              {
                     if (_gamePad == null)
                     {
                            CheckControllerConnection();
                     }
                     else
                     {
                            CheckControllerStillConnected();
                     }
              };

              _connectionTimer.Start();

              // Первая проверка сразу
              CheckControllerConnection();

              // Запускаем WinForms message loop
              Application.Run();
       }

       // ============================================================
       // Проверка подключения контроллера
       // ============================================================

       private static void CheckControllerConnection()
       {
              if (_notifyIcon == null)
                     return;

              lock (_gamePadLock)
              {
                     // Контроллер уже подключён
                     if (_gamePad != null)
                            return;

                     try
                     {
                            var devices = DualSense.EnumerateControllers();

                            foreach (var device in devices)
                            {
                                   try
                                   {
                                          ConnectController(device);
                                          return;
                                   }
                                   catch
                                   {
                                          try
                                          {
                                                 device.Release();
                                          }
                                          catch
                                          {
                                                 // Игнорируем ошибку освобождения
                                          }
                                   }
                            }

                            SetDisconnectedState();
                     }
                     catch
                     {
                            SetDisconnectedState();
                     }
              }
       }

       // ============================================================
       // Подключение DualSense
       // ============================================================

       private static void ConnectController(DualSense controller)
       {
              controller.Acquire();

              _gamePad = controller;

              // SetConnectedState();

              /*
               * ВАЖНО:
               *
               * DualSenseAPI рекомендует использовать BeginPolling()
               * после Acquire().
               *
               * Именно polling обновляет InputState.
               */

              controller.BeginPolling(50);

              UpdateGamepadStatus();
              SetConnectedState();
       }

       private static void CheckControllerStillConnected()
       {
              if (_gamePad == null)
                     return;

              try
              {
                     var devices = DualSense.EnumerateControllers();

                     bool controllerFound = false;

                     foreach (var device in devices)
                     {
                            controllerFound = true;

                            try
                            {
                                   device.Release();
                            }
                            catch
                            {
                            }

                            break;
                     }

                     if (!controllerFound)
                     {
                            DisconnectController();
                            return;
                     }

                     UpdateGamepadStatus();
              }
              catch
              {
                     DisconnectController();
              }
       }

       private static void UpdateGamepadStatus()
       {
              if (_gamePad == null)
                     return;

              try
              {
                     var state = _gamePad.InputState;
                     var batteryStatus = state.BatteryStatus;

                     int rawLevel = (int)batteryStatus.Level;

                     Console.WriteLine(
                         $"Battery: Level={batteryStatus.Level}, " +
                         $"Charging={batteryStatus.IsCharging}, " +
                         $"Full={batteryStatus.IsFullyCharged}"
                     );

                     _batteryLevel = batteryStatus.IsCharging
                         ? 100
                         : Math.Clamp(rawLevel * 10, 0, 100);

                     _isCharging = batteryStatus.IsFullyCharged;

                     UpdateTrayIcon();
              }
              catch
              {
                     DisconnectController();
              }
       }

       // ============================================================
       // Обновление иконки
       // ============================================================

       private static void UpdateTrayIcon()
       {
              if (_notifyIcon == null)
                     return;

              string chargeText = _isCharging
                  ? $"DualSense: Charging ({_batteryLevel}%)"
                  : $"DualSense: {_batteryLevel}%";

              _notifyIcon.Text = chargeText;

              var oldIcon = _notifyIcon.Icon;

              _notifyIcon.Icon = CreateBatteryIcon(
                  _batteryLevel,
                  _isCharging
              );

              if (oldIcon != null &&
                  oldIcon != SystemIcons.Application &&
                  oldIcon != SystemIcons.Error)
              {
                     oldIcon.Dispose();
              }
       }

       // ============================================================
       // Состояние "подключён"
       // ============================================================

       private static void SetConnectedState()
       {
              if (_notifyIcon == null)
                     return;

              _notifyIcon.Text = "DualSense: Connected";

       }

       // ============================================================
       // Состояние "отключён"
       // ============================================================

       private static void SetDisconnectedState()
       {
              if (_notifyIcon == null)
                     return;

              _notifyIcon.Text = "DualSense: Disconnected";

              var oldIcon = _notifyIcon.Icon;

              _notifyIcon.Icon = SystemIcons.Error;

              if (oldIcon != null &&
                  oldIcon != SystemIcons.Application &&
                  oldIcon != SystemIcons.Error)
              {
                     oldIcon.Dispose();
              }

       }

       // ============================================================
       // Отключение DualSense
       // ============================================================

       private static void DisconnectController()
       {
              lock (_gamePadLock)
              {
                     if (_gamePad == null)
                     {
                            SetDisconnectedState();
                            return;
                     }

                     var controller = _gamePad;

                     _gamePad = null;

                     try
                     {
                            controller.EndPolling();
                     }
                     catch
                     {
                            // Игнорируем ошибку
                     }

                     try
                     {
                            controller.Release();
                     }
                     catch
                     {
                            // Игнорируем ошибку
                     }

                     _batteryLevel = 0;
                     _isCharging = false;

                     SetDisconnectedState();
              }
       }

       // ============================================================
       // Создание иконки батареи
       // ============================================================

       private static Icon CreateBatteryIcon(
    int percentage,
    bool isCharging)
       {
              percentage = Math.Clamp(percentage, 0, 100);

              using var bitmap = new Bitmap(16, 16);

              using var graphics = Graphics.FromImage(bitmap);

              graphics.Clear(Color.Transparent);

              graphics.SmoothingMode =
                  System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

              graphics.PixelOffsetMode =
                  System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

              // ============================================================
              // Размеры батареи
              // ============================================================

              const int batteryX = 1;
              const int batteryY = 3;

              const int batteryWidth = 12;
              const int batteryHeight = 10;

              const int terminalX = 13;
              const int terminalY = 6;
              const int terminalWidth = 2;
              const int terminalHeight = 4;

              // ============================================================
              // Цвет заполнения
              // ============================================================

              Brush fillBrush;

              if (isCharging)
              {
                     fillBrush = Brushes.LimeGreen;
              }
              else if (percentage <= 20)
              {
                     fillBrush = Brushes.Red;
              }
              else if (percentage <= 50)
              {
                     fillBrush = Brushes.Orange;
              }
              else
              {
                     fillBrush = Brushes.White;
              }

              // ============================================================
              // Контур батареи
              // ============================================================

              using var borderPen = new Pen(
                  Color.White,
                  1.2f
              );

              graphics.DrawRectangle(
                  borderPen,
                  batteryX,
                  batteryY,
                  batteryWidth,
                  batteryHeight
              );

              // Контакт батареи
              using var terminalBrush = new SolidBrush(Color.White);

              graphics.FillRectangle(
                  terminalBrush,
                  terminalX,
                  terminalY,
                  terminalWidth,
                  terminalHeight
              );

              // ============================================================
              // Заполнение батареи
              // ============================================================

              const int padding = 2;

              int innerWidth = batteryWidth - padding * 2;
              int innerHeight = batteryHeight - padding * 2;

              int fillWidth =
                  (int)Math.Round(innerWidth * percentage / 100.0);

              if (fillWidth > 0)
              {
                     graphics.FillRectangle(
                         fillBrush,
                         batteryX + padding,
                         batteryY + padding,
                         fillWidth,
                         innerHeight
                     );
              }

              // ============================================================
              // Молния при зарядке
              // ============================================================

              if (isCharging)
              {
                     using var lightningBrush =
                         new SolidBrush(Color.Yellow);

                     Point[] lightning =
                     {
            new Point(8, 4),
            new Point(5, 9),
            new Point(8, 9),
            new Point(6, 13),
            new Point(11, 7),
            new Point(8, 7)
        };

                     graphics.FillPolygon(
                         lightningBrush,
                         lightning
                     );
              }

              // ============================================================
              // Превращаем Bitmap в Icon
              // ============================================================

              IntPtr hIcon = bitmap.GetHicon();

              try
              {
                     using var temporaryIcon =
                         Icon.FromHandle(hIcon);

                     return (Icon)temporaryIcon.Clone();
              }
              finally
              {
                     DestroyIcon(hIcon);
              }
       }

       // ============================================================
       // Освобождение Windows HICON
       // ============================================================

       [DllImport(
           "user32.dll",
           CharSet = CharSet.Auto,
           SetLastError = true
       )]
       private static extern bool DestroyIcon(IntPtr hIcon);

       // ============================================================
       // Завершение программы
       // ============================================================

       private static void ExitApplication()
       {
              _connectionTimer?.Stop();

              lock (_gamePadLock)
              {
                     if (_gamePad != null)
                     {
                            try
                            {
                                   _gamePad.EndPolling();
                            }
                            catch
                            {
                                   // Игнорируем ошибку
                            }

                            try
                            {
                                   _gamePad.Release();
                            }
                            catch
                            {
                                   // Игнорируем ошибку
                            }

                            _gamePad = null;
                     }
              }

              if (_notifyIcon != null)
              {
                     _notifyIcon.Visible = false;

                     var icon = _notifyIcon.Icon;
                     var iconHandle = icon.Handle;


                     _notifyIcon.Dispose();

                     if (icon != null &&
                         icon != SystemIcons.Application &&
                         icon != SystemIcons.Error)
                     {
                            icon.Dispose();
                     }
              }

              _connectionTimer?.Dispose();

              Application.Exit();
       }
}
