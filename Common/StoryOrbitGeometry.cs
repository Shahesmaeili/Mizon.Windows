using System;
using System.Numerics;

namespace Mizon.Windows.Common;

/// <summary>Independent photo/paper contours traced in the 1600 × 1200 reference coordinate space.</summary>
internal static class StoryOrbitGeometry
{
    // Visible boundaries are traced from the reference; curves obscured by
    // lettering/badges are smoothly completed. Do not scale Photo to make Contour.
    public static ReadOnlySpan<Vector2> Photo(int variant) => variant switch
    {
        0 => LeftPhoto, 1 => MiddlePhoto, _ => RightPhoto
    };

    public static ReadOnlySpan<Vector2> Contour(int variant) => variant switch
    {
        0 => LeftOutline, 1 => MiddleOutline, _ => RightOutline
    };

    private static readonly Vector2[] LeftPhoto =
    [
        new(392f, 415.5f), new(410.679f, 415.5f), new(426.945f, 417.454f),
        new(442f, 430f), new(470.801f, 454.001f), new(491.235f, 563.139f),
        new(494f, 600f), new(495.889f, 625.19f), new(499.51f, 684.11f),
        new(458f, 680.5f), new(428.345f, 677.921f), new(399.546f, 667.432f),
        new(370f, 663f), new(329.897f, 656.985f), new(290.417f, 654.684f),
        new(250f, 653f), new(230.967f, 652.207f), new(199.982f, 654.883f),
        new(187f, 636f), new(180.938f, 627.183f), new(198.4f, 606.545f),
        new(202f, 600f), new(206.753f, 591.358f), new(209f, 579.81f),
        new(209f, 570f), new(209f, 543.432f), new(187.373f, 516.51f),
        new(194f, 490f), new(197.836f, 474.656f), new(211.575f, 466.393f),
        new(225f, 460f), new(276.828f, 435.32f), new(333.919f, 415.5f),
        new(392f, 415.5f)
    ];

    private static readonly Vector2[] LeftOutline =
    [
        new(392f, 415.5f), new(412.7f, 413.775f), new(431.794f, 416.495f),
        new(448f, 430f), new(479.866f, 456.555f), new(502.918f, 568.365f),
        new(505f, 610f), new(506.512f, 640.242f), new(514.333f, 685.867f),
        new(479f, 700f), new(466.961f, 704.816f), new(423.323f, 691.998f),
        new(410f, 689f), new(360.648f, 677.896f), new(310.391f, 674.84f),
        new(260f, 674f), new(234.714f, 673.579f), new(203.478f, 676.522f),
        new(182f, 660f), new(162.161f, 644.739f), new(196.358f, 606.417f),
        new(198f, 590f), new(200.988f, 560.12f), new(172.883f, 514.703f),
        new(184f, 490f), new(193.506f, 468.876f), new(228.463f, 460.179f),
        new(247f, 454f), new(293.991f, 438.336f), new(342.274f, 419.644f),
        new(392f, 415.5f)
    ];

    private static readonly Vector2[] MiddlePhoto =
    [
        new(756f, 504f), new(796.159f, 504.359f), new(837.341f, 508.285f),
        new(876f, 520f), new(894.071f, 525.476f), new(911.644f, 532.562f),
        new(929f, 540f), new(979.842f, 561.789f), new(1005.206f, 616.937f),
        new(949f, 650f), new(935.374f, 658.015f), new(918.947f, 665.027f),
        new(905f, 672f), new(842.69f, 703.155f), new(781.31f, 737.915f),
        new(710f, 739.5f), new(641.95f, 741.012f), new(647.267f, 714.653f),
        new(650f, 660f), new(651.011f, 639.782f), new(650.505f, 620.202f),
        new(650f, 600f), new(649.651f, 586.028f), new(646.384f, 518.094f),
        new(665f, 510f), new(666.792f, 509.221f), new(747.676f, 503.926f),
        new(756f, 504f)
    ];

    private static readonly Vector2[] MiddleOutline =
    [
        new(720f, 510f), new(772.093f, 507.395f), new(782.055f, 505.735f),
        new(835f, 515f), new(855.23f, 518.54f), new(875.38f, 522.932f),
        new(895f, 529f), new(925.858f, 538.544f), new(926.88f, 543.694f),
        new(951f, 555f), new(955.204f, 556.971f), new(960.111f, 557.464f),
        new(964f, 560f), new(968.406f, 562.874f), new(978.854f, 575.505f),
        new(982f, 580f), new(1004.237f, 611.767f), new(994.504f, 639.952f),
        new(963f, 660f), new(936.55f, 676.832f), new(907.425f, 688.141f),
        new(879f, 701f), new(824.087f, 725.842f), new(771.66f, 755.917f),
        new(710f, 759f), new(686.46f, 760.177f), new(650.012f, 763.095f),
        new(635f, 740f), new(625.766f, 725.794f), new(631.231f, 705.377f),
        new(632f, 690f), new(633.829f, 653.422f), new(634.832f, 616.638f),
        new(633f, 580f), new(632.164f, 563.288f), new(629.324f, 544.011f),
        new(641f, 530f), new(655.58f, 512.504f), new(698.779f, 511.061f),
        new(720f, 510f)
    ];

    private static readonly Vector2[] RightPhoto =
    [
        new(1260f, 416.5f), new(1334.605f, 419.073f), new(1297.715f, 484.922f),
        new(1318f, 530f), new(1325.262f, 546.137f), new(1340.838f, 561.408f),
        new(1356f, 570f), new(1361.065f, 572.87f), new(1366.525f, 575.023f),
        new(1372f, 577f), new(1378.546f, 579.364f), new(1387.078f, 578.078f),
        new(1392f, 583f), new(1403.021f, 594.021f), new(1389.052f, 628.997f),
        new(1383f, 640f), new(1357.321f, 686.689f), new(1314.952f, 715.5f),
        new(1260f, 715.5f), new(1181.689f, 715.5f), new(1163.115f, 663.717f),
        new(1144f, 600f), new(1118.655f, 515.518f), new(1122.794f, 444.605f),
        new(1225f, 420f), new(1232.098f, 418.291f), new(1250.853f, 416.185f),
        new(1260f, 416.5f)
    ];

    private static readonly Vector2[] RightOutline =
    [
        new(1260f, 399f), new(1336.571f, 404.743f), new(1291.788f, 484.407f),
        new(1314f, 530f), new(1325.405f, 553.41f), new(1354.394f, 569.725f),
        new(1377f, 580f), new(1386.309f, 584.231f), new(1397.531f, 584.271f),
        new(1406f, 590f), new(1420.435f, 599.765f), new(1396.584f, 648.028f),
        new(1390f, 660f), new(1359.519f, 715.42f), new(1302.318f, 742.674f),
        new(1240f, 738f), new(1155.579f, 731.668f), new(1135.956f, 671.823f),
        new(1118f, 600f), new(1114.656f, 586.622f), new(1109.705f, 573.526f),
        new(1107f, 560f), new(1092.561f, 487.807f), new(1124.586f, 424.282f),
        new(1200f, 406f), new(1218.563f, 401.5f), new(1240.806f, 397.56f),
        new(1260f, 399f)
    ];
}
