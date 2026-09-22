# -*- coding: utf-8 -*-
"""テスト用の LZH (-lh0-, レベル 1 ヘッダ) を組み立てる。

LZH を作れるツールが手元に無いので、仕様どおりにバイト列を組む。
-lh0- は無圧縮なので、圧縮器を書かずに正しい書庫を作れる。

ヘッダにはチェックサムが、データには CRC-16 が入っていて 7-Zip は両方を検証する。
読めて中身が一致した時点で、構造は正しいと言える。

レベル 1 を使う理由: LHA for Windows や Lhaplus が実際に吐くのがこの形式で、
ディレクトリは拡張ヘッダ (種別 0x02) で表現される。
レベル 0 はファイル名欄に 0xFF 区切りのパスを入れる古い形式で、
7-Zip はそれをパスとして解釈しないためフォルダが潰れてしまう。

LZH にはファイル名の文字コードを記録する場所が無い。
日本語の書庫は CP932 のバイト列がそのまま入っている。この資材もそれに倣う。

レベル 1 ヘッダ:
     0  1  基本ヘッダサイズ  = offset 2 以降、最初の拡張ヘッダ長までの長さ
     1  1  チェックサム      = 同じ範囲の総和 mod 256
     2  5  圧縮法            "-lh0-"
     7  4  スキップサイズ    = データ長 + 基本ヘッダより後ろの拡張ヘッダ長
    11  4  元のサイズ
    15  4  MS-DOS 日時
    19  1  属性              0x20
    20  1  レベル            1
    21  1  ファイル名長
    22  n  ファイル名（パスを含まない）
  22+n  2  データの CRC-16
  24+n  1  OS 識別子         'M'
  25+n  2  最初の拡張ヘッダの長さ（ここまでが基本ヘッダ）
"""
import io
import struct
import sys

DELIMITER = 0xFF          # LZH のパス区切り
EXT_DIRECTORY = 0x02      # 拡張ヘッダ種別: ディレクトリ名


def crc16(data: bytes) -> int:
    """LHA が使う CRC-16/ARC（反転多項式 0xA001、初期値 0）。"""
    crc = 0
    for byte in data:
        crc ^= byte
        for _ in range(8):
            crc = (crc >> 1) ^ 0xA001 if crc & 1 else crc >> 1
    return crc


def dos_timestamp(year=2003, month=5, day=1, hour=12, minute=0, second=0) -> int:
    date = ((year - 1980) << 9) | (month << 5) | day
    time = (hour << 11) | (minute << 5) | (second // 2)
    return (date << 16) | time


def extension_blocks(directory_bytes: bytes):
    """拡張ヘッダを (最初の長さ, 基本ヘッダより後ろのバイト列) にして返す。

    レベル 1 では、最初の拡張ヘッダの「長さ」だけが基本ヘッダの末尾に入り、
    種別・内容・次の長さは基本ヘッダの外に続く。
    ここを取り違えると 7-Zip はヘッダ長が合わず、書庫ごと弾く。

    1 ブロックの長さ = 種別 1 + 内容 + 次の長さ 2。
    """
    if not directory_bytes:
        return 0, b''

    payload = directory_bytes + bytes([DELIMITER])   # 末尾にも区切りを置く
    size = 3 + len(payload)
    trailing = bytes([EXT_DIRECTORY]) + payload + struct.pack('<H', 0)

    return size, trailing


def entry(name: str, data: bytes) -> bytes:
    directory, _, base = name.rpartition('/')

    base_bytes = base.encode('cp932')
    directory_bytes = b''
    if directory:
        directory_bytes = bytes([DELIMITER]).join(
            part.encode('cp932') for part in directory.split('/'))

    first_size, trailing = extension_blocks(directory_bytes)

    body = b'-lh0-'
    body += struct.pack('<I', len(data) + len(trailing))   # スキップサイズ
    body += struct.pack('<I', len(data))                   # 元のサイズ
    body += struct.pack('<I', dos_timestamp())
    body += bytes([0x20])                                  # 属性
    body += bytes([0x01])                                  # ヘッダレベル 1
    body += bytes([len(base_bytes)])
    body += base_bytes
    body += struct.pack('<H', crc16(data))
    body += b'M'                                           # OS 識別子
    body += struct.pack('<H', first_size)

    header_size = len(body)
    checksum = sum(body) & 0xFF

    return bytes([header_size, checksum]) + body + trailing + data


def build(path: str, entries):
    with io.open(path, 'wb') as f:
        for name, text in entries:
            f.write(entry(name, text.encode('utf-8')))

        f.write(bytes([0x00]))   # 基本ヘッダサイズ 0 が終端


if __name__ == '__main__':
    out = sys.argv[1]
    build(out, [
        ('日本語の名前.txt', 'LZH テスト'),
        ('サブフォルダ/読みかた.txt', 'よみかた'),
        ('readme.txt', 'plain ascii name'),
    ])

    with io.open(out, 'rb') as f:
        data = f.read()

    print(out, len(data), 'bytes')
