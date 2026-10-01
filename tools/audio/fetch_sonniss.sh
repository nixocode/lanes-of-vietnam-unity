#!/bin/zsh
# Fetch the Sonniss #GameAudioGDC files the game uses (ASSETS.md) from the
# official mirror into SourceArt/audio/sonniss<year>, and check their hashes.
# They are licensed, not CC0: they stay out of git (the owner's rule for a
# public repo), and tools/audio/slice_shots.py builds the game's audio from them.
set -eu
here="${0:A:h}"; art="${here:h:h}/SourceArt/audio"
Y=2017
q() { python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$1"; }
fetch() { # folder file sha256 (from the bundle year in Y)
  local dst="$art/sonniss$Y"; mkdir -p "$dst"
  local url="https://ftpmirror.your.org/pub/misc/sonniss$Y/individual/$(q "$1")/$(q "$2")"
  [[ -f "$dst/$2" ]] || curl -sf --max-time 1800 -o "$dst/$2" "$url"
  echo "$3  $dst/$2" | shasum -a 256 -c -
}
fetch "Gamemaster Audio -  Explosion Sound Pack" "explosion_large_08.wav" aa26f39d6e9fe6e6942e15fd4260f8da62f201451784995b718b9abfd2af712c
fetch "Gamemaster Audio -  Explosion Sound Pack" "explosion_med_long_tail_01.wav" fea8024edfe07c5dbb40db53021dcc1c08b88415783bfe27767e3dffd794c08e
fetch "Gamemaster Audio -  Explosion Sound Pack" "explosion_large_no_tail_03.wav" 7e541b20048559e711d10eb1766e8b4717e6c1feb4529e0adebe0d96fbcaba87
fetch "Gamemaster Audio -  Bullet Impact Sounds" "bullet_flyby_fast_05.wav" aaed15696f96ab7e9f6e844eb7b063156720c43abe1fdf88abf3bb50cf0ce7f7
fetch "Gamemaster Audio -  Bullet Impact Sounds" "bullet_impact_body_thump_02.wav" cb84da810687e966e7ed0ae3da269c65b42231bed22dd52696e4619c413e16ab
fetch "Charlie Atanasyan - Thailand sound library by Faunethic" "Jungle quiet insects and birds wide _120407_11.wav" df1da8303fb61f1f8bc371c05a60faf00ab75783f5c3d14126c48244baef8b6e
# Machine gun, distant guns, and the rounds going by (the Warfare Library).
fetch "Pole Position - The Warfare Library" "warfare_t2_mg_firing_close_projectile_tail_large_field_Telinga_w_MKH8020_or_MKH8060.wav" b7cb77aa2f8af5ca9b19d3e817d277b9455f4fb8a4dedcfa046bb7096c2f1bf3
fetch "Pole Position - The Warfare Library" "warfare_t1b_cannon_firing_forest_distant_MKH8060_2.wav" 3cca62528291adea28c12739dcc7532306606198c60a116bc5a2010a9978afe1
fetch "Pole Position - The Warfare Library" "warfare_t3_mg_whizzes_ricochets_bullet_cracks_M10.wav" 806acf0c97a53f1c643540c802ce29b64302ce77ffe69cc7350210fc6de9c312

Y=2016   # the Browning .30 cal, in service in Vietnam with the ARVN and early US units
fetch "Pole Position Production - M1919A4 Browning Machine Gun .30cal" "M1919A4_Browning_Machine_Gun_.30cal_5m_behind_ORTF_blanks_Triple_shots_x_1.wav" 35e04fb9302cca6be1a21014588b2eabcc9ff9d9f3d2e552399dd4369295d5c2
fetch "Pole Position Production - M1919A4 Browning Machine Gun .30cal" "M1919A4_Browning_Machine_Gun_.30cal_200m_left_behind_blanks_Triple_shots_x_1.wav" 01ecc109521c4203e3807174bfe7a9bdf83b7d5f2885feac90963da0c0af11e4

Y=2019
fetch "Airborne Sound - Battlefield Howitzers" "Howitzer,M101,C1,105 mm,Distant,Right Side,Shot,Pound,Thick.wav" 21d5791d1b147ed68ba1223035499dfd9be1381a0da391c53235fd1ddf8584f6
fetch "Airborne Sound - Battlefield Howitzers" "Howitzer,M101,C3,105 mm,Distant,Right Side,Shot,Explode,Crack,Sweetener.wav" 3b5a39a6a77f9e55b1c09acd6866a3872c25043c85427b4c433d7f1207e59a2a
fetch "Airborne Sound - Battlefield Howitzers" "Howitzer,M101,C3,105 mm,Medium Distant,Right Side,Shot,Firm,Heavy,EQ Version,Soft Attack.wav" 86270f9b2446539b0bfcf2629914962f2ceb23e98ef7f59fe3526b21f1a72fc1
fetch "Airborne Sound - Jet Fighter Maneuvers" "Jet,Fighter,CF-18,Hornet,By,Slow,Rip to Whistle.wav" 588f6a272d6a3d7624ecf52324556dd6e5ad5f8c005afe44e279109a45b2fd86
fetch "Airborne Sound - Jet Fighter Maneuvers" "Jet,Fighter,F-16,Fighting Falcon,Medium Distant,By,Whip,Buffet.wav" 6bce891e2b8da6aa3d0d02edac4c33fd6a359dae5f139d515c5e9ffb329daae4
fetch "Red Libraries - Bodyfall" "RL_bodyfall_Dirt_M4_Close_Stereo_Hard_Impact_10.wav" 90d5ae7ee9f62593c8902c2e8008cc2a6a769d3232957807531d47c693272a99
fetch "Apple Hill Studios - Military Radio Voices" "Military Radio Voice A (HHG) Troops In Contact Message.wav" daac19ceddd5245012e478bca0fce9a2c1769e18502960e8a6ad738ae250fc5d

Y=2020
fetch "Pole Position - Tokarev PPSh-41 submachine gun" "PPSh41, Firing, t1, Burst, Long, MKH416.wav" 238ad768ac01ff6073094622e3451b5742bf57bc2b7b14b9bd4b05e22916f828
fetch "PMSFX - Bullet Bys &Impacts" "PM_BBI_Bullet_Impact_Dirt_3.wav" ecaa352573b7233ca8a167b4f24ee96ec068be8b106374a40d27d18e29121b63
fetch "PMSFX - Bullet Bys &Impacts" "PM_BBI_Bullet_Impact_Hit_Body_Flesh_25.wav" 0a7c2e4be3a087c05d04f8ce7e14b9bdccb4b3e79c5acd5cf34147692a70ee73
fetch "PMSFX - Bullet Bys &Impacts" "PM_BBI_Bullet_Passby_Whizzby_Airy_5.wav" 9c697f86741f117a7f5cb100b699c4a1cc3090f7ee2458cb8134cb21fc95065e
