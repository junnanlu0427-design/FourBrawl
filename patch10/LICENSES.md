# 第十版画面素材来源与许可

游戏公开发布，所以只用了可商用、可再分发的素材。放进项目后的位置：`Assets/Brawl/Resources/Brawl10/`。

| 文件 | 来源 | 许可 | 说明 |
|---|---|---|---|
| `Fonts/MaShanZheng.ttf` | 马善政楷书 Ma Shan Zheng，Copyright 2018 The Ma Shan Zheng Project Authors，https://github.com/google/fonts/tree/main/ofl/mashanzheng | SIL Open Font License 1.1（无保留字体名） | 用 fontTools 按游戏里出现过的字做了子集（约 1300 字，870 KB）。许可全文：`Fonts/OFL-MaShanZheng.txt`，必须随游戏一起分发。 |
| `Fonts/ZcoolQingKe.ttf` | 站酷庆科黄油体 ZCOOL QingKe HuangYou，Copyright 2018 The ZCOOL QingKe HuangYou Project Authors，https://github.com/google/fonts/tree/main/ofl/zcoolqingkehuangyou | SIL Open Font License 1.1（无保留字体名） | 同上做了子集（580 KB）。许可全文：`Fonts/OFL-ZcoolQingKeHuangYou.txt`。 |
| `Fx/*.png`（七芒星神纹、符文环、放射光、光柱、星芒、光翼、毛笔刀痕、光鸟、羽毛） | 本项目原创，Claude 用 `Tools/ArtGen/gen_art.py` 程序生成 | 随本游戏使用 | 神纹上的「光水地日鸟人息 / 一…七」是用上面的马善政字体写的。 |
| `Text/*.png`（七日神谕字卡、神降、居合、见切、剑神、终结、出场字卡） | 本项目原创，同一脚本用上面两款 OFL 字体渲染 | 随本游戏使用（字形部分仍受 OFL 约束，OFL 允许把字体渲染成图片使用） | |
| `Ui/logo.png`、`Ui/vignette.png`、`Ui/fade_down.png` | 本项目原创，同一脚本生成 | 随本游戏使用 | |
| `Sfx/*.wav`（神降、七日、居合、出场、终结、界面悬停/点击） | 本项目原创，Claude 用 `Tools/ArtGen/gen_sfx.py` 合成（锯齿波合唱 + 钟 + 噪声） | 随本游戏使用 | |

说明：Kenney、OpenGameArt 这次在云端环境里访问不到（网络策略拦截），所以没有用它们的素材；除两款 OFL 字体外全部是自己生成的，不存在第三方图片 / 音频的授权问题。

## 再生成
```
pip install fonttools pillow numpy
python3 Tools/ArtGen/gen_art.py <输出目录> MaShanZheng-Regular.ttf ZCOOLQingKeHuangYou-Regular.ttf
python3 Tools/ArtGen/gen_sfx.py <输出目录>/Sfx
pyftsubset <字体>.ttf --text-file=chars.txt   # chars.txt = 所有 .cs 里出现过的字 + ASCII
```
字体子集缺字时（比如玩家自己起的名字），`Ui.Text` 会自动退回系统字体，不会显示成方块。
