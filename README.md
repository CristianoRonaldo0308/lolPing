# lolPing — bản MVP cục bộ

Tiện ích Windows lấy cảm hứng từ vòng tín hiệu trong game MOBA: giữ phím kích hoạt, kéo chuột trái tới một hướng và thả để phát hoạt ảnh + âm thanh ngay tại chỗ, trên bất kỳ ứng dụng nào.

Biểu tượng được vẽ bằng vector và âm thanh được tổng hợp bằng code, nên dự án **không chứa tài sản của Riot hay trò chơi nào**.

## Tải về (cho bạn bè)

**[⬇ Tải lolPing.exe](https://github.com/CristianoRonaldo0308/lolPing/releases/latest/download/lolPing.exe)**: nhấp đúp để chạy trên Windows 10/11, không cần cài .NET.

- Nếu Windows SmartScreen cảnh báo "Windows protected your PC", chọn **More info → Run anyway**. App chưa được ký số nên Windows chưa nhận diện.
- Muốn vào phòng của bạn bè: mở mục **Phòng**, dán mã (hoặc lời mời) bạn bè gửi, rồi bấm **Vào phòng**.
- Mỗi lần có cập nhật, tải lại file ở link trên.

## Có gì trong bản này

- **Vòng tín hiệu 8 hướng + ô giữa**: giữ `Alt` (đổi được thành Win/Ctrl/Shift) rồi kéo. Nếu giữ phím và nhấp mà không kéo, ứng dụng đặt tín hiệu ở ô giữa; nếu tắt tuỳ chọn này, cú nhấp được trả lại cho ứng dụng bên dưới. Nhấp chuột phải khi đang kéo để huỷ.
- **Vòng biểu cảm**: giữ `Ctrl` rồi kéo. Có 11 biểu cảm, và bạn thêm được tối đa 24 ảnh riêng (PNG/JPG/GIF/WebP, ≤ 8 MB, ≤ 8192 px).
- **Tuỳ chỉnh vòng chọn**: kéo-thả mục vào ô, đổi chỗ các ô, kéo ô ra ngoài để làm trống. Chuột phải vào một ô để **gán ứng dụng** (mở exe/lối tắt) hoặc **gán phím tắt** (gửi tổ hợp phím).
- **Hoạt ảnh**: biểu tượng nảy lên, vòng sáng lan toả và tên của bạn hiện bên dưới. Kích thước, thời gian hiển thị và độ lớn vòng chọn chỉnh được.
- **Âm thanh riêng**: trong *Tín hiệu & âm thanh → Âm thanh riêng*, chọn một thư mục chứa .ogg/.wav/.mp3. App đọc file tại chỗ và ghép theo tên: `Caution_ping_SFX.ogg` → Nguy hiểm, `On_My_Way_ping_SFX.ogg` → Đang tới… hoặc đặt đúng id như `danger.wav`, `e_laugh.ogg`. Tín hiệu không có file vẫn dùng âm tổng hợp.
- **Âm thanh**: mỗi tín hiệu có âm riêng. Các âm được trộn với nhau nên bấm liên tiếp không bị cắt. Có điều chỉnh âm lượng.
- **Nhiều màn hình và DPI khác nhau**: mỗi màn hình có một lớp phủ riêng (Per-Monitor V2).
- **Tạm dừng khi ứng dụng toàn màn hình** đang ở phía trước (game, video).
- **Phím tắt bật/tắt toàn cục** `Ctrl + Alt + P`, biểu tượng ở khay hệ thống và tuỳ chọn khởi động cùng Windows.
- **Giao diện tiếng Việt / tiếng Anh**, với khung i18n sẵn sàng thêm ngôn ngữ và hỗ trợ bố cục RTL.

## Yêu cầu

- Windows 10 1809+ hoặc Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

> Vì sao .NET 10 thay vì .NET 8: .NET 8 hết hỗ trợ ngày 10/11/2026, còn .NET 10 là bản LTS hiện tại và có sẵn giao diện Fluent tối của Windows 11 cho WPF (`ThemeMode="Dark"`).

## Cài đặt nhanh (có biểu tượng trên Desktop)

Nhấp đúp `install.cmd`. Script sẽ build bản Release, cài vào `%LOCALAPPDATA%\Programs\lolPing`, tạo lối tắt trên Desktop và Start menu rồi mở app. Chạy lại mỗi khi có bản cập nhật; dữ liệu của bạn được giữ nguyên.

## Chạy thử (khi phát triển)

```powershell
cd lolPing
dotnet run --project src/LolPing
```

Hoặc mở `lolPing.slnx` bằng Visual Studio 2022 17.14+ hoặc Rider rồi nhấn F5.

Để đóng gói thành một file exe duy nhất:

```powershell
dotnet publish src/LolPing -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# kết quả: src/LolPing/bin/Release/net10.0-windows/win-x64/publish/lolPing.exe
```

Ứng dụng lưu dữ liệu ở `%AppData%\lolPing\` (gồm `settings.json`, thư mục `images\` và `log.txt`). Khi gặp lỗi, hãy mở `log.txt` trước tiên.

## Cấu trúc mã

```
src/LolPing/
├─ App.xaml(.cs)          Khởi động, một phiên bản duy nhất, nối các phần lại với nhau
├─ Core/
│  ├─ Models.cs           AppSettings (CommunityToolkit.Mvvm), WheelSlot, Hotkey
│  ├─ SettingsStore.cs    Đọc/ghi JSON (gom nhiều lần ghi), log, khởi động cùng Windows
│  ├─ Loc.cs              Bảng chuỗi + {c:Tr Key} cho XAML, đổi ngôn ngữ trực tiếp
│  ├─ Icons.cs            Biểu tượng tín hiệu và biểu cảm vẽ bằng vector
│  └─ Catalog.cs          Danh mục mục có sẵn + nhập/xoá ảnh riêng
├─ Audio/Sounds.cs        Bộ tổng hợp âm + bộ trộn NAudio
├─ Input/
│  ├─ GestureEngine.cs    Hook chuột cấp thấp và máy trạng thái cử chỉ
│  ├─ InputSim.cs         SendInput (phím giả chặn Start menu, gửi lại cú nhấp, phím tắt), phát hiện toàn màn hình
│  ├─ HotkeyManager.cs    RegisterHotKey
│  └─ Native.cs           P/Invoke
├─ Overlay/
│  ├─ OverlayWindow.cs    Cửa sổ trong suốt, xuyên chuột cho từng màn hình và hoạt ảnh tín hiệu
│  ├─ OverlayManager.cs   Chọn màn hình, quy đổi điểm ảnh ↔ DIP
│  └─ WheelView.cs        Vẽ vòng chọn (dùng chung cho lớp phủ và trình chỉnh sửa)
├─ UI/                    Cửa sổ Cài đặt, trình chỉnh vòng, ô nhập phím tắt, khay hệ thống
└─ Lang/vi.json, en.json  Thêm ngôn ngữ: tạo file mới với cùng khoá và đăng ký trong Loc.Languages
```

**Luồng xử lý một cử chỉ:** hook nhận `WM_LBUTTONDOWN` khi đang giữ phím kích hoạt, chặn cú nhấp và gửi một phím giả (`0xE8`) để việc nhả Win/Alt không mở Start menu hay thanh menu. Khi chuột di chuyển quá ngưỡng, vòng chọn hiện ra. Góc giữa vị trí chuột và tâm quyết định ô được chọn. Khi nhả chuột, ứng dụng phát hoạt ảnh + âm thanh tại điểm bắt đầu kéo và chạy hành động đã gán (nếu có). Hook chỉ làm phép tính nhẹ; phần vẽ được đẩy qua Dispatcher.

## Lưu ý đã biết

- Bản này **chưa được biên dịch** trong môi trường mình viết, vì ở đó không tải được .NET SDK. Lần build đầu có thể còn vài lỗi nhỏ; gửi mình nguyên văn thông báo lỗi để sửa.
- `Ctrl` + kéo trong Explorer vốn dùng để sao chép tệp. Với phím biểu cảm mặc định là `Ctrl`, thao tác đó sẽ mở vòng biểu cảm. Bạn có thể đổi phím trong Cài đặt nếu thấy vướng.
- Ứng dụng chạy với quyền thường nên không vẽ hay nhận chuột trên cửa sổ chạy quyền quản trị. Game ở chế độ toàn màn hình độc quyền cũng che lớp phủ.
- Ảnh GIF chỉ hiện khung hình đầu. Ảnh WebP cần tiện ích *WebP Image Extensions* của Windows.

## Phòng chơi chung

Mục **Phòng** trong Cài đặt cho bạn **Tạo phòng** rồi gửi mã `PING-XXXXX-XXXXX` cho bạn bè (tối đa 8 người). Khi bạn ping trên máy mình, tín hiệu, biểu cảm và âm thanh hiện ngay trên màn hình của mọi người trong phòng, kèm tên và màu nhãn của bạn.

- **Cùng wifi/LAN:** không cần cấu hình gì. Lần đầu mở, Windows hỏi quyền tường lửa: chọn **Allow** (ít nhất mạng Private).
- **Khác mạng (Internet):** cần một server relay. Xem hướng dẫn bên dưới, sau đó dán địa chỉ server vào ô **Server Internet**. Nút **Sao chép mã** sẽ tạo lời mời dạng `PING-XXXXX-XXXXX @ lolping-relay.onrender.com`. Bạn bè dán lời mời này vào ô vào phòng là app tự đặt server luôn.
- **Âm thanh meme và ảnh riêng** được gửi kèm ở lần dùng đầu (mỗi file tối đa 3 MB). Người nhận lưu lại ở `%AppData%\lolPing\cache`, những lần sau chỉ gửi mã.
- Ô có gán ứng dụng/phím tắt chỉ hiện trên máy bạn, không gửi vào phòng.
- **Mã hoá đầu cuối:** khoá AES-256-GCM được dẫn xuất từ mã phòng bằng PBKDF2 + HKDF. Server và người cùng mạng chỉ thấy RoomId (mã băm), không đọc được nội dung.

### Tạo server relay miễn phí (Render)

1. Đưa thư mục dự án lên một repo GitHub (có thể để private).
2. Vào https://render.com, đăng nhập bằng GitHub, chọn **New → Blueprint** rồi chọn repo. Render tự đọc file `render.yaml` và build `src/LolPing.Relay` bằng Docker.
3. Khi trạng thái là **Live**, sao chép địa chỉ dạng `https://lolping-relay-xxxx.onrender.com`. Mở trang đó, nếu thấy `lolPing relay OK` là server đã chạy.
4. Trong lolPing, dán địa chỉ vào **Phòng → Server Internet**.

Gói miễn phí của Render "ngủ" sau khoảng 15 phút không có người dùng. Lần kết nối đầu sau đó có thể mất khoảng 1 phút; app hiện "Internet: đang kết nối…" rồi tự vào.

**Cách khác:**
- Chạy `run-relay.cmd` trên một máy luôn bật, mở cổng 8080 trên router, rồi dùng địa chỉ `ws://<IP công khai>:8080`.
- Chạy bằng Docker trên VPS: `docker build -t lolping-relay src/LolPing.Relay && docker run -d -p 8080:8080 lolping-relay`.

