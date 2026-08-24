@echo off
echo RiverBox 插件发布脚本
echo ====================

REM 编译插件
echo 1. 编译插件...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo 编译失败！
    pause
    exit /b 1
)

REM 创建发布目录
echo 2. 创建发布目录...
if not exist "release" mkdir release
if not exist "release\RiverBox" mkdir "release\RiverBox"

REM 复制必要的文件
echo 3. 复制插件文件...
copy "RiverBox\bin\Release\net8.0\RiverBox.dll" "release\RiverBox\"
copy "RiverBox\icon.png" "release\RiverBox\"
copy "RiverBox\RiverBox.deps.json" "release\RiverBox\"

REM 创建ZIP文件
echo 4. 创建ZIP文件...
cd release
powershell -command "Compress-Archive -Path 'RiverBox' -DestinationPath 'RiverBox.zip' -Force"
cd ..

echo.
echo 发布准备完成！
echo 请将 release\RiverBox.zip 上传到GitHub Release
echo.
echo 下一步：
echo 1. 访问 https://github.com/Whswa-river/River/releases/new
echo 2. 上传 release\RiverBox.zip
echo 3. 更新 RiverBox.json 中的版本号
echo.
pause
