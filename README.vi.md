# SeraphicSense (Tiếng Việt)
<p align="center">
  <a href="https://github.com/Xingeriaa/SeraphicSense/actions/workflows/dotnet.yml"><img src="https://img.shields.io/github/actions/workflow/status/Xingeriaa/SeraphicSense/dotnet.yml?branch=main&label=build" alt="Build Status" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/releases"><img src="https://img.shields.io/github/v/release/Xingeriaa/SeraphicSense?display_name=tag" alt="Latest Release" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/releases"><img src="https://img.shields.io/github/downloads/Xingeriaa/SeraphicSense/total" alt="Total Downloads" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Xingeriaa/SeraphicSense" alt="License" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/issues"><img src="https://img.shields.io/github/issues/Xingeriaa/SeraphicSense" alt="Issues" /></a>
</p>

<p align="center">
  <a href="README.md">Trang chính</a>
  |
  <a href="README.en.md">English</a>
</p>

## Tổng quan
SeraphicSense là một công cụ chạy nền trên Windows, có hành vi xác định rõ ràng:

1. Giữ các file bắt buộc luôn tồn tại trong thư mục theo dõi.
2. Xóa các file bị cấm trong thư mục đó.

Ứng dụng được viết bằng WPF, có tray icon và cơ chế giám sát nền.

## Cơ chế hoạt động chính
Ứng dụng theo dõi một thư mục cấu hình trước và áp dụng các quy tắc:

- Tên gốc bắt buộc: `MatureData-WindowsClient`
- Đuôi bắt buộc: `.pak`, `.sig`, `.ucas`, `.utoc`
- Tên gốc bị cấm: `VNGLogo-WindowsClient` (mọi đuôi)

Khi có sự kiện tạo/xóa/đổi tên file, ứng dụng chờ theo độ trễ cấu hình (mặc định `2000` ms), sau đó kiểm tra:

1. Nếu thiếu file bắt buộc thì copy từ thư mục nguồn.
2. Nếu có file khớp `VNGLogo-WindowsClient*` thì xóa.

## Tính năng chính
- Theo dõi thư mục bằng `FileSystemWatcher`
- Độ trễ kiểm tra có thể chỉnh (mặc định `2000 ms`)
- Có retry khi file bị khóa tạm thời
- Tích hợp khay hệ thống (`Open`, `Start/Stop`, `Check Updates`, `Exit`)
- Tự khởi động cùng Windows (HKCU Run)
- Khởi động thu nhỏ xuống tray
- Chế độ một phiên bản duy nhất (mở lần 2 sẽ gọi lại app đang chạy)
- Cập nhật qua GitHub với 2 kiểu:
  - Cập nhật ứng dụng (dùng installer)
  - Cập nhật dữ liệu (chỉ tải file dữ liệu)

## Cấu hình
Vị trí file cấu hình:

- `%AppData%\SeraphicSense\config.json`

Các trường chính:

- `ObservedFolderPath`
- `SourceFolderPath`
- `RequiredBaseName`
- `RequiredExtensions`
- `ForbiddenBaseName`
- `ValidationDelayMs`
- `StartWithWindows`
- `StartMinimized`
- `AutoStartMonitoring`
- `CheckUpdatesOnLaunch`
- `GitHubRepository` (cố định: `https://github.com/Xingeriaa/SeraphicSense.git`)
- `LastAppliedDataReleaseTag`

## Hệ thống cập nhật
Ứng dụng kiểm tra release mới nhất trên GitHub và phân loại kiểu cập nhật:

### Cập nhật ứng dụng
Được chọn khi release có asset installer (`.exe` hoặc `.msi`) và được xem là mới hơn.

Luồng xử lý:

1. Tải installer vào `%TEMP%\SeraphicSense\updates\...`
2. Chạy installer ở chế độ im lặng
3. Thoát phiên bản hiện tại

### Cập nhật dữ liệu
Được chọn khi release có dữ liệu phù hợp mà không cần cài lại toàn bộ ứng dụng.

Asset dữ liệu hỗ trợ:

- File zip có tên liên quan dữ liệu (ví dụ `BackupPaks.zip`, `MatureData.zip`, `data-*.zip`)
- Hoặc file trực tiếp có đuôi: `.pak`, `.sig`, `.ucas`, `.utoc`

Luồng xử lý:

1. Tải và giải nén/copy file vào `SourceFolderPath`
2. Lưu `LastAppliedDataReleaseTag`
3. Chạy kiểm tra ngay để đồng bộ thư mục theo dõi

### Gắn nhãn kiểu cập nhật trong release notes (tùy chọn)
Bạn có thể ép kiểu cập nhật bằng nội dung release:

- `update-type: data` hoặc `[update-type:data]`
- `update-type: app` hoặc `[update-type:app]`

## Cài đặt
### Cách 1: Dùng installer (khuyến nghị)
Tải bản cài mới nhất tại:

- `https://github.com/Xingeriaa/SeraphicSense/releases`

Nếu thư mục mục tiêu yêu cầu quyền cao, hãy chạy installer với quyền Administrator.

### Cách 2: Chạy bản portable
Dùng bản publish `win-x64` và chạy trực tiếp `SeraphicSense.exe`.

## Build từ source
Yêu cầu:

- .NET SDK 9.0+
- Windows (WPF target: `net9.0-windows`)

Build:

```powershell
dotnet restore
dotnet build -c Release
```

Publish self-contained:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## Build installer (Inno Setup)
Repository có sẵn file `installer/SeraphicSense.iss`.

Ví dụ:

```powershell
ISCC installer\SeraphicSense.iss /DPublishDir="C:\path\to\publish"
```

## Lưu ý vận hành
- Nếu không ghi được vào thư mục bảo vệ, hãy chạy ứng dụng bằng quyền Administrator.
- Game có thể ghi đè hoặc khóa file trong lúc cập nhật.
- Ứng dụng có cơ chế retry để xử lý khóa file tạm thời.

## Repository
- Repository chính: `https://github.com/Xingeriaa/SeraphicSense.git`
- CI workflow: `.github/workflows/dotnet.yml`

## License
Dự án tuân theo giấy phép trong file `LICENSE`.
