// Borrowed from:
// https://github.com/RustAudio/lewton/blob/bb2955b717094b40260902cf2f8dd9c5ea62a84a/dev/cmp/src/lib.rs

// Vorbis decoder written in Rust
//
// Copyright (c) 2016-2017 est31 <MTest31@outlook.com>
// and contributors. All rights reserved.
// Licensed under MIT license, or Apache 2 license,
// at your option. Please see the LICENSE file
// attached to this source distribution for details.

namespace NVorbis.Tests;

public static class TestAssets
{
    public static IEnumerable<TestAssetDef> get_asset_defs()
    {
        yield return new(
            filename: "bwv_1043_vivace.ogg",
            hash: new Sha256(0x839249e4_6220321e, 0x2bbb1106_e30d0bef, 0x4acd800d_3827a482, 0x743584f313c8c671),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/wiki/bwv_1043_vivace.ogg?raw=true");
        yield return new(
            filename: "bwv_543_fuge.ogg",
            hash: new Sha256(0xc5de55fe_3613a88b, 0xa1622a1c_931836c0, 0xaf5e9bf3_afae9514, 0x18a07975_a16e7421),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/wiki/bwv_543_fuge.ogg?raw=true");
        yield return new(
            filename: "maple_leaf_rag.ogg",
            hash: new Sha256(0xf66f18de_6bc79126, 0xf13d9683_1619d68d, 0xdd56f952_7e50e105, 0x8be0754b_479ee350),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/wiki/maple_leaf_rag.ogg?raw=true");
        yield return new(
            filename: "hoelle_rache.ogg",
            hash: new Sha256(0xbbdf0a8d_4c151aee, 0x5a21fb71_ed86894b, 0x1aae5c7d_ba9ea767, 0xf7af6c0f_752915c2),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/wiki/hoelle_rache.ogg?raw=true");
        yield return new(
            filename: "thingy-floor0.ogg",
            hash: new Sha256(0x02b9e947_64db30b8, 0x76964eba_2d0a813d, 0xedaecdbf_a978a13d, 0xc9cef9bd_c1f4e9ee),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/thingy-floor0.ogg");
        yield return new(
            filename: "audio_simple_err.ogg",
            hash: new Sha256(0x1b97b2b1_51b34f1c, 0xa6868aa0_08853579, 0x2252aa5c_7c990e1d, 0xe9eedd6a_33d3c0dd),
            url: "https://github.com/RustAudio/lewton/files/1543593/audio_simple_err.zip");
    }

    public static IEnumerable<TestAssetDef> get_libnogg_asset_defs()
    {
        yield return new(
            filename: "6-mode-bits-multipage.ogg",
            hash: new Sha256(0xe68f06c5_8a812593, 0x3d869d4c_831ee298, 0x500b1894_ab27dc48, 0x41f075c1_04093c27),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/6-mode-bits-multipage.ogg");
        yield return new(
            filename: "6-mode-bits.ogg",
            hash: new Sha256(0x48ec7d1b_3284ea8c, 0xdb9a3511_f4f1dd4d, 0x8170be94_82dd7c0e, 0x8edb4980_2318e1c8),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/6-mode-bits.ogg");
        yield return new(
            filename: "6ch-all-page-types.ogg",
            hash: new Sha256(0xc965f1f0_3be8af38, 0x69d22fca_d41bbde0, 0x111ba187_48f7c9b2, 0x7fb46e4a_33b857cd),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/6ch-all-page-types.ogg");
        yield return new(
            filename: "6ch-long-first-packet.ogg",
            hash: new Sha256(0x7ac5d89b_9cc69762, 0xdd191d17_778b0625, 0x462d4a2c_5488d10e, 0x8aef6a77_e990c7f7),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/6ch-long-first-packet.ogg");
        yield return new(
            filename: "6ch-moving-sine-floor0.ogg",
            hash: new Sha256(0x95443ad7_c16f3dc7, 0xf66ce34a_eb3ec90f, 0x14fb717c_79326e8f, 0x3c92826f_5c0606e1),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/6ch-moving-sine-floor0.ogg");
        yield return new(
            filename: "6ch-moving-sine.ogg",
            hash: new Sha256(0x05dae404_fc266671, 0x598aaf2f_d55f52d5, 0x63e7f266_31abbd34, 0x87cc9b63_d458500d),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/6ch-moving-sine.ogg");
        yield return new(
            filename: "bad-continued-packet-flag.ogg",
            hash: new Sha256(0x8c93b1ec_92746b4c, 0x9eb8c855_e65218f1, 0x4cd8c302_4ddea5b3, 0x264c8a66_fee7d6ee),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/bad-continued-packet-flag.ogg");
        yield return new(
            filename: "bitrate-123.ogg",
            hash: new Sha256(0x8cbb82b8_eab2e4d4, 0x115b62f6_2323c85d, 0x05e23525_19677996, 0xd7a1e53c_01ebb436),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/bitrate-123.ogg");
        yield return new(
            filename: "bitrate-456-0.ogg",
            hash: new Sha256(0x2ae12b96_3c333164, 0xf1fbbbf4_fc75bc9a, 0xa6b5b428_9a9791ba, 0x9a3a7c27_5d725571),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/bitrate-456-0.ogg");
        yield return new(
            filename: "bitrate-456-789.ogg",
            hash: new Sha256(0x326bb289_c1dcc4f7, 0x9ee00719_9a8124dd, 0x14bfea4a_1f7fb94f, 0x9c7f9394_c0b53584),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/bitrate-456-789.ogg");
        yield return new(
            filename: "empty-page.ogg",
            hash: new Sha256(0x51010e14_b84dee56, 0x2b76d3a4_ddb760d4, 0xb3a740d7_8714b11b, 0x6307e2de_73ee6d48),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/empty-page.ogg");
        yield return new(
            filename: "large-pages.ogg",
            hash: new Sha256(0x53b63f96_61ddf726, 0xbd0b9d19_33b7fe54, 0xef102487_42354ff2, 0x15b294f3_4ac0aec0),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/large-pages.ogg");
        yield return new(
            filename: "long-short.ogg",
            hash: new Sha256(0x96a16644_6ee171f9, 0xdf3b7a47_01567d19, 0xe52d8cfc_593e17ca, 0x4f8697dd_e551de63),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/long-short.ogg");
        yield return new(
            filename: "noise-6ch.ogg",
            hash: new Sha256(0x879ab419_c0a848b0, 0xd17b1d6b_9d8557c4, 0xcec35ffb_0d973371, 0x743eeee7_0c6f7e17),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/noise-6ch.ogg");
        yield return new(
            filename: "noise-stereo.ogg",
            hash: new Sha256(0x53c9e52b_f47f89d2, 0x92644e4c_d467da70, 0xc49709fd_cc7a2c99, 0xd171b4e1_883a0499),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/noise-stereo.ogg");
        yield return new(
            filename: "partial-granule-position.ogg",
            hash: new Sha256(0xd42765b7_6989a74f, 0xc9071d08_2409a34e, 0x9a4c603e_ecb51b25, 0x3d4d026a_9751580e),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/partial-granule-position.ogg");
        yield return new(
            filename: "sample-rate-max.ogg",
            hash: new Sha256(0xc758248c_dc2d2ed6, 0x7ed80f27_e8436565, 0xfd5b94c5_c662a88a, 0xb9a5fabd_6d23ff04),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/sample-rate-max.ogg");
        yield return new(
            filename: "single-code-2bits.ogg",
            hash: new Sha256(0xee1eb710_f37709fe, 0xa87d1fac_6cdb6ab8, 0x6012497c_50dae58d, 0x237b6607_165656b9),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/single-code-2bits.ogg");
        yield return new(
            filename: "single-code-nonsparse.ogg",
            hash: new Sha256(0x9fbbbe8b_a4988d83, 0x62a66f42_52fe8f52, 0x8e41ac65_9db23f37, 0x7a9166bb_a843bb7b),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/single-code-nonsparse.ogg");
        yield return new(
            filename: "single-code-ordered.ogg",
            hash: new Sha256(0x27e53ee9_8f277340, 0x5b98f6ad_402aad2d, 0x0be3d3fd_7439acab, 0x720f46c7_3957bc93),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/single-code-ordered.ogg");
        yield return new(
            filename: "single-code-sparse.ogg",
            hash: new Sha256(0x3327a0eb_7287bc2d, 0xf2a10932_446930c9, 0xda6fa338_3b0a63a8, 0x1cfd04f8_32e160a6),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/single-code-sparse.ogg");
        yield return new(
            filename: "sketch008-floor0.ogg",
            hash: new Sha256(0x64fc1efe_12609a05, 0x44448021_c730c283, 0x2a758637_982a4698, 0xa138f138_a1417b5c),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/sketch008-floor0.ogg");
        yield return new(
            filename: "sketch008.ogg",
            hash: new Sha256(0xd0b34d94_a5379edc, 0x6eb63374_3ecd187d, 0x81b02e53_54fed989, 0xf35320ec_fd32be71),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/sketch008.ogg");
        yield return new(
            filename: "sketch039.ogg",
            hash: new Sha256(0xc595bec5_d9bad010, 0x3527f779_dba13837, 0x743866c1_5ff5ad7a, 0xeedc6dab_49516d9a),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/sketch039.ogg");
        yield return new(
            filename: "split-packet.ogg",
            hash: new Sha256(0xe5e55988_45d733a1, 0xefaeb258_a6c07563, 0xaf8dd302_04117044, 0xd25df13c_d2944e0e),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/split-packet.ogg");
        yield return new(
            filename: "square-interleaved.ogg",
            hash: new Sha256(0x305a7031_87f5ad84, 0xfacbf5b8_990007cf, 0xe93d4035_e3efb9f8, 0x60149676_04374356),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/square-interleaved.ogg");
        yield return new(
            filename: "square-multipage.ogg",
            hash: new Sha256(0x691988c4_fefe850d, 0xd265fd8c_91f2e90c, 0x92f94578_ce18c800, 0x5b40e882_77dc26c9),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/square-multipage.ogg");
        yield return new(
            filename: "square-stereo.ogg",
            hash: new Sha256(0xb2eb353c_dd9ddd3f, 0x80964747_4e1a8bff, 0x6913e28b_53cac52a, 0x54906f7f_f203501f),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/square-stereo.ogg");
        yield return new(
            filename: "square-with-junk.ogg",
            hash: new Sha256(0x51ede72d_e15b2998, 0xcf8de6dd_3f57a6ab, 0xc280f927_8d775f0f, 0x5fec2d26_79e341b6),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/square-with-junk.ogg");
        yield return new(
            filename: "square.ogg",
            hash: new Sha256(0x31c9fa7d_2f374ebf, 0x9ffc1c21_e95b8369, 0xc2ced3ae_230151c0, 0x0208613c_a308d812),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/square.ogg");
        // Omit thingy-floor0.ogg
        yield return new(
            filename: "thingy.ogg",
            hash: new Sha256(0x646f0523_5723aa09, 0xe69e123c_73560ffb, 0x753f9ffe_00e3c54b, 0x99f8f0c1_cd583707),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/thingy.ogg");
        yield return new(
            filename: "zero-length.ogg",
            hash: new Sha256(0xaa71c872_18f6dec5, 0x1383110b_c1b77d20, 0x4bbb21f7_867e2cc8, 0x28304241_7522b330),
            url: "http://achurch.org/hg/libnogg/raw-file/tip/tests/data/zero-length.ogg");
    }

    public static IEnumerable<TestAssetDef> get_xiph_asset_defs_1()
    {
        yield return new(
            filename: "1.0-test.ogg",
            hash: new Sha256(0x9a882710_314bcc1d, 0x2b4cdefb_7f89911f, 0x4375acd1_feb9e64a, 0x12eca9f7_202377d3),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/1.0-test.ogg?raw=true");
        yield return new(
            filename: "1.0.1-test.ogg",
            hash: new Sha256(0x8c9423e0_0826d6d2, 0x457d78c0_9d6e2a94, 0xbcdf9216_af796bb2, 0x2a4c4f45_0c2e72bb),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/1.0.1-test.ogg?raw=true");
        yield return new(
            filename: "48k-mono.ogg",
            hash: new Sha256(0xf51459d9_bdd04ca3, 0xec6f6732_b8a01efc, 0xdc83e5c6_fc79c7d5, 0x347527bf_d4948e9e),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/48k-mono.ogg?raw=true");
        yield return new(
            filename: "beta3-test.ogg",
            hash: new Sha256(0x7fc791a4_d5a0d3b7, 0xcef24480_93ccf2ae, 0x54600acc_81667be9, 0x0c6b209c_366c32ea),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/beta3-test.ogg?raw=true");
        yield return new(
            filename: "beta4-test.ogg",
            hash: new Sha256(0xbc367c0d_4dcdbf1f, 0x0a2f81ab_f74cc52c, 0x8f0ab836_6d150a75, 0x830782ca_2dba080a),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/beta4-test.ogg?raw=true");
    }

    public static IEnumerable<TestAssetDef> get_xiph_asset_defs_2()
    {
        yield return new(
            filename: "bimS-silence.ogg",
            hash: new Sha256(0xe2a38871_d390ed65, 0x1faf0fec_5253a687, 0x3530da4f_2503d850, 0x21c99325_dfd23813),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/bimS-silence.ogg?raw=true");
        yield return new(
            filename: "chain-test1.ogg",
            hash: new Sha256(0xd9c37533_a1f456d2, 0xa996755a_43d112ef, 0x46b6ef95_33199139, 0x07bc79f1_c014d79e),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/chain-test1.ogg?raw=true");
        yield return new(
            filename: "chain-test2.ogg",
            hash: new Sha256(0x5b5bf834_e93e9a93, 0xb7114be0_84161e97, 0x2f33d203_19635184, 0x92ba2c91_dc4418a7),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/chain-test2.ogg?raw=true");
        yield return new(
            filename: "chain-test3.ogg",
            hash: new Sha256(0xed039ba7_75d1b31e, 0x805d26d6_413a3ef2, 0xc6663bf4_c5ea47ce, 0x1462f8f2_3c84d8dc),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/chain-test3.ogg?raw=true");
        yield return new(
            filename: "highrate-test.ogg",
            hash: new Sha256(0x0942c883_69f84b12, 0x5388cc84_37575b66, 0xd67bc97b_aaeeb997, 0xb6602144_821509df),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/highrate-test.ogg?raw=true");
    }

    public static IEnumerable<TestAssetDef> get_xiph_asset_defs_3()
    {
        yield return new(
            filename: "lsp-test.ogg",
            hash: new Sha256(0xad1b07b6_8576ae2c, 0x85178475_ef3607e1, 0xa96c09e9_6296cae8, 0x3ee7a972_6f8311eb),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/lsp-test.ogg?raw=true");
        yield return new(
            filename: "lsp-test2.ogg",
            hash: new Sha256(0x7b00f893_a93071bd, 0xf243b8a2_2c1758a6, 0xab09c1ab_8eee89ca, 0x21659d98_ec0f53ea),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/lsp-test2.ogg?raw=true");
        yield return new(
            filename: "lsp-test3.ogg",
            hash: new Sha256(0x7a5c4064_fc31285f, 0x6fea0b24_24a0d415, 0xa8b18ef1_3d1aa1ef, 0x931a429a_b9a593d1),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/lsp-test3.ogg?raw=true");
        yield return new(
            filename: "lsp-test4.ogg",
            hash: new Sha256(0xcb0b2893_1dfc8ef8, 0xb2d320f3_028fab54, 0x8171e7c6_e94006f0, 0xc2353247_8a8d3596),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/lsp-test4.ogg?raw=true");
        yield return new(
            filename: "mono.ogg",
            hash: new Sha256(0xd8abca95_445a0718, 0x6c9a158d_4f573f38, 0x918985dd_2498a9bc, 0x8d1811bd_72fe1d1a),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/mono.ogg?raw=true");
    }

    public static IEnumerable<TestAssetDef> get_xiph_asset_defs_4()
    {
        yield return new(
            filename: "moog.ogg",
            hash: new Sha256(0xbd5b51bb_1d6855e0, 0xe990e3eb_dd230fc1, 0x62654078_09bd6db4, 0x4aea691a_da498943),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/moog.ogg?raw=true");
        yield return new(
            filename: "one-entry-codebook-test.ogg",
            hash: new Sha256(0x789b5146_f2a7c086, 0x4a228ee4_a870606a, 0x32c4169e_22097be6, 0x51296237_05c54749),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/one-entry-codebook-test.ogg?raw=true");
        yield return new(
            filename: "out-of-spec-blocksize.ogg",
            hash: new Sha256(0x0970e662_91744815, 0xf2ca0dec_7523f5bb, 0xc112907c_5c75978a, 0x0f91d3b0_ed6f03b2),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/out-of-spec-blocksize.ogg?raw=true");
        yield return new(
            filename: "rc1-test.ogg",
            hash: new Sha256(0xccfac6ac_7c75615b, 0xec0632b9_4bcf088c, 0x027c0b11_2cd136c9, 0x97d04f03_7252cd3d),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/rc1-test.ogg?raw=true");
        yield return new(
            filename: "rc2-test.ogg",
            hash: new Sha256(0x4adcda78_6dfeea41, 0x88d7b1df_35571dbe, 0x29c687f2_d1cac680, 0xed115353_abfe1637),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/rc2-test.ogg?raw=true");
        yield return new(
            filename: "rc2-test2.ogg",
            hash: new Sha256(0x24ade471_eefe1c76, 0x42f73a68_10e56266, 0xb34c2d7d_30a3b5ad, 0x05a34c09_8128226c),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/rc2-test2.ogg?raw=true");
        yield return new(
            filename: "rc3-test.ogg",
            hash: new Sha256(0xedd984e8_4c7c2a59, 0xaf7801f3_e8d2db11, 0xa6619134_fd8d0274, 0xd1289e70_bf60cde7),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/rc3-test.ogg?raw=true");
    }


    public static IEnumerable<TestAssetDef> get_xiph_asset_defs_5()
    {
        yield return new(
            filename: "singlemap-test.ogg",
            hash: new Sha256(0x50d80776_08a4192b, 0x8f8505ae_c0217be8, 0xb6c25def_4068899a, 0xfc19c17f_30e1d521),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/singlemap-test.ogg?raw=true");
        yield return new(
            filename: "sleepzor.ogg",
            hash: new Sha256(0x01c67eca_f7a58b5a, 0xc5f1fe3b_d060b5d6, 0x1536595e_97927a1b, 0x1cf0129a_62b5cfcf),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/sleepzor.ogg?raw=true");
        yield return new(
            filename: "test-short.ogg",
            hash: new Sha256(0x18351055_2403021f, 0xb90ce437_96fcc88e, 0x16c8bb4a_e5d0d72a, 0x316b8e51_afc395fa),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/test-short.ogg?raw=true");
        yield return new(
            filename: "test-short2.ogg",
            hash: new Sha256(0x6bd3f59e_0fa77904, 0xedd35c73_dadd6558, 0xe2a36d78_c9d7bc5d, 0xb223862b_f092fcc2),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/test-short2.ogg?raw=true");
        yield return new(
            filename: "unused-mode-test.ogg",
            hash: new Sha256(0xe27ae6fc_2f7c0037, 0xc2832880_1c46d966, 0xabcee305_0e3ebd40, 0xbf097f79_86f50f94),
            url: "https://github.com/RustAudio/lewton-test-assets/blob/master/xiph/unused-mode-test.ogg?raw=true");
    }

    /// Regression tests for bugs obtained by fuzzing
    ///
    /// The test files are licensed under CC-0:
    /// * https://github.com/RustAudio/lewton/issues/33#issuecomment-419640709
    /// * http://web.archive.org/web/20180910135020/https://github.com/RustAudio/lewton/issues/33
    public static IEnumerable<TestAssetDef> get_fuzzed_asset_defs()
    {
        yield return new(
            filename: "27_really_minimized_testcase_crcfix.ogg",
            hash: new Sha256(0x83f6d6f3_6ae92600, 0x0f064007_e79ef7c4, 0x5ed561e4_9223d9b6, 0x8f980d26_4050d683),
            url: "https://github.com/RustAudio/lewton/files/2363013/27_really_minimized_testcase_crcfix.ogg.zip");
        yield return new(
            filename: "32_minimized_crash_testcase.ogg",
            hash: new Sha256(0x644170cc_c3e48f2e, 0x2bf28cad_ddcd8375, 0x20df0967_1c3d3d99, 0x1b128b9f_db281da6),
            url: "https://github.com/RustAudio/lewton/files/2363080/32_minimized_crash_testcase.ogg.zip");
        yield return new(
            filename: "33_minimized_panic_testcase.ogg",
            hash: new Sha256(0x4812e725_d7c6bdb4, 0x8e745b4e_0a396efc, 0x96ea5cb3_0e304cf9, 0x710dadda_3d963171),
            url: "https://github.com/RustAudio/lewton/files/2363173/33_minimized_panic_testcase.ogg.zip");
        yield return new(
            filename: "35_where_did_my_memory_go_repacked.ogg",
            hash: new Sha256(0x2f202e71_ca0440a2, 0xde4a1544_3beae9d3, 0x230e81e4_7bc01d29, 0x929fc86e_e731887c),
            url: "https://github.com/RustAudio/lewton/files/2889595/35_where-did-my-memory-go-repacked.ogg.zip");
        yield return new(
            filename: "bug-42-sample009.ogg",
            hash: new Sha256(0x7e3d7fd6_d306cd1c, 0x1704d058_6b4e62cc, 0x897c499e_3ffc1911, 0xf62ec0fc_3a062871),
            url: "https://github.com/RustAudio/lewton/files/2905014/bug-42-sample009.ogg.zip");
        yield return new(
            filename: "bug-42-sample012.ogg",
            hash: new Sha256(0x8d92c435_9bbe987b, 0x77459f30_9859b6bb, 0xa0a11724_e71fd5e8, 0x1873c597_ec71d857),
            url: "https://github.com/RustAudio/lewton/files/2905017/bug-42-sample012.ogg.zip");
        yield return new(
            filename: "bug-42-sample015.ogg",
            hash: new Sha256(0x274c1722_2d7cfc10, 0x44d2fee3_e60377ea, 0xc87f5ee8_d952eeaf, 0x3d636b01_6b1db7d3),
            url: "https://github.com/RustAudio/lewton/files/2905018/bug-42-sample015.ogg.zip");
        yield return new(
            filename: "bug-42-sample016.ogg",
            hash: new Sha256(0xab02fd55_a275b1ec, 0x0c6c56a6_67834231, 0xbf34b3a7_9038f431, 0x96d1015c_1555e535),
            url: "https://github.com/RustAudio/lewton/files/2905019/bug-42-sample016.ogg.zip");
        yield return new(
            filename: "bug-42-sample029.ogg",
            hash: new Sha256(0x1436fff4_d8fa61ff, 0x2b22ffd0_21c2bd80, 0xf072556b_8b58cfc7, 0x2fdfc043_4efd9a24),
            url: "https://github.com/RustAudio/lewton/files/2905020/bug-42-sample029.ogg.zip");
        yield return new(
            filename: "bug-44-sample059.ogg",
            hash: new Sha256(0x4c1452e3_87a64090, 0x46513272_4a83f028, 0x46457336_fa58ddc6, 0xee9c6df5_98d756c0),
            url: "https://github.com/RustAudio/lewton/files/2922511/bug-44-sample059.ogg.zip");
        yield return new(
            filename: "bug-44-sample060.ogg",
            hash: new Sha256(0xb8bd4283_1a8922c4, 0xc78ff1ea_5b42ecbb, 0x874135ba_7e7fcd60, 0xc4fff7a4_19d857a4),
            url: "https://github.com/RustAudio/lewton/files/2922512/bug-44-sample060.ogg.zip");
        yield return new(
            filename: "bug-46-sample001.ogg",
            hash: new Sha256(0xd5015f9a_3b79a28b, 0xf621ecc2_e96286c2, 0x0ef742e9_36e256f7, 0x7b8978e6_bce66aad),
            url: "https://github.com/RustAudio/lewton/files/2923287/bug-46-sample001.ogg.zip");
    }
}