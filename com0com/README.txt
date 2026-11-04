com0com 虚拟串口驱动（随程序附带的官方签名安装包）
==================================================

版本
----
com0com 3.0.0.0（i386 + x64，signed）

来源
----
SourceForge 官方项目页：
https://sourceforge.net/projects/com0com/files/com0com/3.0.0.0/

原始压缩包：
com0com-3.0.0.0-i386-and-x64-signed.zip
SHA256: 6E5D4359865277430D4AE88C73FB7E648A0ED8E81AEA5002478179CFCB0BB0E1

本目录内的文件（由上述压缩包解压而来）
--------------------------------------
Setup_com0com_v3.0.0.0_W7_x64_signed.exe
  261,544 字节
  SHA256: 26486B28604B49A9008C54FEB11B9ECE0008A8287EE5CAF0BCF2A62F4317128F

Setup_com0com_v3.0.0.0_W7_x86_signed.exe
  255,400 字节
  SHA256: DE917425E022839F6374B509E3D34F3F9E9773915A44996194442337EADF4A5A

数字签名
--------
签名者：CN=CyberCircuits, O=CyberCircuits, C=NZ
用 PowerShell 验证：
  Get-AuthenticodeSignature .\Setup_com0com_v3.0.0.0_W7_x64_signed.exe
状态应为 Valid。

安装
----
一般不需要手动操作：打开 SerialProxy，点右上角「环境准备」，
按向导点「安装 com0com」并允许 UAC 即可。

手动安装：
  1. 64 位系统运行 Setup_com0com_v3.0.0.0_W7_x64_signed.exe，
     32 位系统运行 Setup_com0com_v3.0.0.0_W7_x86_signed.exe；
  2. 安装时保留默认选项（会安装签名驱动）；
  3. 安装完成后，在程序的「环境准备」里点「重新检测」；
  4. 在「创建虚拟串口对」里确认 COM3 ⇔ COM4，点「创建端口对」。

  Windows 7 上如果提示驱动签名问题，请先安装系统更新，
  不要为了这个工具关闭驱动签名强制。

卸载
----
控制面板 → 程序和功能 → com0com → 卸载。
卸载前先在本工具里「停止」，并关闭占用虚拟串口的程序。

许可
----
com0com 由 Vyacheslav Frolov 开发，采用 GPL v2 发布，
版权归原作者所有。本程序只是原样附带其官方安装包，
未做任何修改，仅用于本机创建虚拟串口对。

项目主页：https://sourceforge.net/projects/com0com/
