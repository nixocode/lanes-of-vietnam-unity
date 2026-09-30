#!/bin/zsh
# Fetch the Sonniss #GameAudioGDC 2017 files the game uses (ASSETS.md) from the
# official mirror into SourceArt/audio/sonniss2017, and check their hashes.
# They are licensed, not CC0: they stay out of git (the owner's rule for a
# public repo), and tools/audio/slice_shots.py builds the game's audio from them.
set -eu
here="${0:A:h}"; dst="${here:h:h}/SourceArt/audio/sonniss2017"; mkdir -p "$dst"
B="https://ftpmirror.your.org/pub/misc/sonniss2017/individual"
fetch() { # folder file sha256
  local url="$B/${1// /%20}/${2// /%20}"
  [[ -f "$dst/$2" ]] || curl -sf --max-time 900 -o "$dst/$2" "$url"
  echo "$3  $dst/$2" | shasum -a 256 -c -
}
fetch "Gamemaster Audio -  Explosion Sound Pack" "explosion_large_08.wav" aa26f39d6e9fe6e6942e15fd4260f8da62f201451784995b718b9abfd2af712c
fetch "Gamemaster Audio -  Explosion Sound Pack" "explosion_med_long_tail_01.wav" fea8024edfe07c5dbb40db53021dcc1c08b88415783bfe27767e3dffd794c08e
fetch "Gamemaster Audio -  Explosion Sound Pack" "explosion_large_no_tail_03.wav" 7e541b20048559e711d10eb1766e8b4717e6c1feb4529e0adebe0d96fbcaba87
fetch "Gamemaster Audio -  Bullet Impact Sounds" "bullet_flyby_fast_05.wav" aaed15696f96ab7e9f6e844eb7b063156720c43abe1fdf88abf3bb50cf0ce7f7
fetch "Gamemaster Audio -  Bullet Impact Sounds" "bullet_impact_body_thump_02.wav" cb84da810687e966e7ed0ae3da269c65b42231bed22dd52696e4619c413e16ab
fetch "Charlie Atanasyan - Thailand sound library by Faunethic" "Jungle quiet insects and birds wide _120407_11.wav" df1da8303fb61f1f8bc371c05a60faf00ab75783f5c3d14126c48244baef8b6e
