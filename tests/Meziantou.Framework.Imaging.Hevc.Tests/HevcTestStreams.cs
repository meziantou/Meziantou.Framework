namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Small HEVC byte streams (Rec. ITU-T H.265, Annex B) written by the pinned encoders, kept as literals so that the unit
/// tests need no tool. Each one carries the decoded picture hash of its pictures, which is what the tests compare the
/// decoded planes with; the planes were also compared with those of the reference decoders when the streams were made.
/// </summary>
internal static class HevcTestStreams
{
    /// <summary>HM 18.0, 24x72, 4:4:4, 8 bits, 5 pictures in a random access structure of 2 pictures, asymmetric partitions and scaled motion vector predictors: --QP=37 --AMP=1 --MaxNumMergeCand=4 --WeightedPredP=1 --CbQpOffset=7 --CrQpOffset=-6 --LoopFilterDisable=1.</summary>
    public static byte[] HmAmp444 => Convert.FromBase64String(
        "AAAAAUABDAL//wQIAAADAAgIAAADAAC6AAC8igSAAAAAAUIBAgQIAAADAAgIAAADAAC6AACQGQJLLeRbkaYzqprXKt2yAAAAAUQBwaVDhqGkgA" +
        "AAASYBrwtgz2PHxqSLGGdEENWrr4eszcpVmF24YRCbEtSXNOyLvptkrpGGWtCzvgf/1FgGSAZEIvmdIq7HvmT5iqvnC0YA6QCbaj/vzYPckeG6" +
        "FZvVYzZHMgRYoRyvYEBIyUa6c7yHGJrGZnbJupp5Ihw7ML3EUCJGtRiGE9F/cfBSKjBwIBRamDqGHTnj6Mhpk9KmsTis05MgZib11PyiTDeDMZ" +
        "9WqyFseZQ9IvgWtUY2Aoi6MQWV6mscnxA7xD9A/V4/mXE/yE/2xaq/4aE77gfYOeDWozghk0oVDZeyGNzuD9p297z4l4ngicDuMo0e114DWkpj" +
        "fGhZprLX2EbNZsRYangVNsWsj4FbvXPWSNJb1Lky45lL/wyqqKdG6ofqCZB8Z/B3T2cU37fykSNFmxOulM8AAAFQAYQxAK4Merinlxi4hCmZYv" +
        "+W9y7+XmhpK5HNr5BkADWnQJ1WFon4JIec4USfCseEPkFnXoAAAAABAgHgVfxBzFWgkkv9L7LYSeRjKk5W+yjivzPPvwURNUCn7UE22Ra4U6m2" +
        "5VmZ587r8BCf3DwJnM1XM1rjEy/kAAABUAGEMQBMsvsZcd4IjzO/lLk+glpDd2iJBrKHmV5U9ydoV6PFNc/VZIoIrs6vfCKxWbHVYAqAAAAAAQ" +
        "QC4DcCg1ApnDKREfUw1lpQAAABUAKEMQC/I0ho6xCgdhOg45oYhf0ZsaW96XYGQF2SspcsUChSVF5iO0E/0eGwyvCd5FPQ2h2AAAAAAQIB4JHa" +
        "g5j4ABQed/7dbASpcDPQPXhy+LI6ms8QMFp5w6aHUDwpMtyZ1sNqAmSACPCxB4q6/RZXqE14CbhmVh+9rteXmlL9WK/9k+wEkML1H5hm65dzAW" +
        "9yIMkzOQnhHJk1Cdc9eReRbQtjyLwMcyQFE7HPutn3NlEwBUqm5gPxmSfBna4AAAFQAYQxALh6oHYNxN/lcd37fsocTQp3xfENp/J2l5fgvk6H" +
        "LdHBDAh4NqGkpLx7HPpgcsE6CYAAAAABBALgcwqDUA/EiZLHgAAAAVAChDEAvUM1PvCxDHmhSJ6T0o2dsUAzVQeEggj+7u/cxcdHNs3bKUHcOp" +
        "K+ARUbPEP7rnv+gA==");

    /// <summary>HM 18.0, 48x24, 4:4:4, 10 bits, 10 pictures in a random access structure of 4 pictures, cross-component prediction in inter coding units: --QP=15 --MaxDeltaQP=1 --ConstrainedIntraPred=1 --ResidualRotation=1 --GolombRiceParameterAdaptation=1 --CrossComponentPrediction=1.</summary>
    public static byte[] HmCrossComponentInter444TenBits => Convert.FromBase64String(
        "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");

    /// <summary>HM 18.0, 16x8, 4:2:2, 12 bits, checksum hashes: --SEIDecodedPictureHash=3 --QP=35.</summary>
    public static byte[] HmChecksum422TwelveBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqwiJFKX6ttntkAAAABRAHBkJWBEgAAASYBrwlg+09JeJuun/" +
        "ksL4dwPfmYwq8xRm7rvEILBUcYaLlAuhlfCjeLHkqXao8pWIohOT5gja/nq6cc9/kM5vdQ1VuyT7HeHaAAAAFQAYQNAgAAOu0AACUpAAAfEYA=");

    /// <summary>HM 18.0, 10x19 (a conformance window in a 16x24 picture), 4:4:4, 10 bits, two pictures: --QP=34 --ImplicitResidualDPCM=1 --ResidualRotation=1 --SingleSignificanceMapContext=1 --MaxCUChromaQpAdjustmentDepth=1.</summary>
    public static byte[] HmChromaQpOffsets444TenBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqQEQznmbL9yNM9tgOBAAAAAUQBwZCVgRYCqMGcAAABJgGvCH" +
        "D9R9vfuZFe4NBDxbr10tGPU+EuAvjTUBfy2zBMWjt4urzKP0PzFcyapO/ngSpVlvTCRRzqI3OyNItZrDAucyvGuE0Pf9laZ0m5iiWWQFo7dB76" +
        "W0qVBTGPv+8MQ9ZDo+fClJnrLgQMQ7bSIqLP0JVMYeEUBm75m9QJETWAWxrqZD3BjMctwpVU4yaJwp5EiA5BxIfpeUAAAAFQAYQxAA16PE/tKd" +
        "IV+YAW/CpnhqdVLhbTylIVYc/8xhTDkpZjyjGtHJMBhIR6TaNf8wH2uYAAAAABAgHYDcIc/Ufb37mRXuDQQ8W69dLRj1PhLgL401AX8tswTFo7" +
        "eLq8yj9D8xXMmqTv54EqVZb0wkUc6iNzsjSLWawwLnMrxrhND3/ZWmdJuYollkBaO3Qe+ltKlQUxj7/vDEPWQ6PnwpSZ6y4EDEO20iKiz9CVTG" +
        "HhFAZu+ZvUCRE1gFsa6mQ9wYzHLcKVVOMmicKeRIgOQcSH6XlAAAABUAGEMQANejxP7SnSFfmAFvwqZ4anVS4W08pSFWHP/MYUw5KWY8oxrRyT" +
        "AYSEek2jX/MB9rmA");

    /// <summary>HM 18.0, 16x8, 4:2:0, 10 bits, two pictures, CRC hashes: --SEIDecodedPictureHash=2 --QP=35.</summary>
    public static byte[] HmCrc420TenBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqgiJNl+rbZ7ZAAAAABRAHBkJWBEgAAASYBrwlg+088x8uy+z" +
        "5T2sKnPPYST3GvkXYGerEOoUjwikpxfjcwjMkyXnXqkvElKxFEJyfMEbX89XJfce4WGTMfQ/bwctfbeJAAAAFQAYQHAY8Qw6g2UIAAAAABAgHY" +
        "DcJY+wI/c6rG7qJEZtkQcgmN7L3aQszp24V9fmsMipub+TR20B2u0AN29U8qFEuFhl4l+9NuF/EUbtr5Pq/m+5izREGYGtv7K6n6G6D4bgAAAV" +
        "ABhAcBz9n2g26tgA==");

    /// <summary>HM 18.0, 104x16, 4:2:0, 12 bits, 5 pictures in a random access structure of 8 pictures, several slices and dependent slice segments in a picture: --QP=36.</summary>
    public static byte[] HmInterSlices420TwelveBits => Convert.FromBase64String(
        "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" +
        "cm7rQoxIBBZrOSIOc133vE94A=");

    /// <summary>HM 18.0, 22x31, 4:0:0, 10 bits, 5 pictures, P slices that refer to up to 4 pictures, two slices in a picture: --QP=22 --MaxNumMergeCand=1 --MaxDeltaQP=3 --SAO=0 --ConstrainedIntraPred=1 --SliceMode=1 --SliceArgument=2.</summary>
    public static byte[] HmLowDelayP400TenBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAgIAAADAAC6lwJAAAAAAUIBAQQIAAADAAgIAAADAAC6wyCHdOWXuTQxf+X/+2wDAgAAAAFEAcGQnvAwhiwBDgAAAS" +
        "YBrE48xQ1mW5VV6IAIokbvtKE4cXVMpsZdSomlxbAb4EbcW7uljI05RDsu38NcBCxmtuoBsTR/Ca/WShcW10RfL61uIKUL74Kji1D9tWAo+q88" +
        "VKc8ckZRTltAYvWVl8MMVTdzcfZz/B4GHgCez5rteK0BRt4i28I3qOE4YtMJFIXljGImSIgFwI97OAAAAVABhBEAMtr5I5AruEhcopdJc8PmY4" +
        "AAAAABAgHQDPFeooQAAAFQAYQRADLa+SOQK7hIXKKXSXPD5mOAAAAAAQIB0BVrK8DmtyhoqCLS0/mFnf5xoAAAAVABhBEA2ZN5oRTxAUpJUqWn" +
        "Y63tQYAAAAABAgHQHe0rwKKEAAABUAGEEQDZk3mhFPEBSklSpadjre1BgAAAAAECAdAkWV5BBcRi8f2/q3cFYms+fdnizhVdEhy2azaV9raLjV" +
        "SNwdHQXUsbT/JxGZEByk9wPmgAAAFQAYQRAKYiV9Mu4/PSc5pV99D/3M2A");

    /// <summary>HmLowDelayP400TenBits rewritten by HevcTestStreamRewriter (SpsCandidate, MsbPresent, ModifyLists): its first picture is a long-term reference picture of every other picture, and the reference picture lists of the pictures of odd picture order count are modified. The pictures are not those of the encoder, and the stream has no hash.</summary>
    public static byte[] HmLongTermRewritten400TenBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAgIAAADAAC6lwJAAAAAAUIBAQQIAAADAAgIAAADAAC6wyCHdOWXuTQxf+X/+6AOwDAgAAAAAUQBwZCe8DCGbAEOAA" +
        "AAASYBrE48xQ1mW5VV6IAIokbvtKE4cXVMpsZdSomlxbAb4EbcW7uljI05RDsu38NcBCxmtuoBsTR/Ca/WShcW10RfL61uIKUL74Kji1D9tWAo" +
        "+q88VKc8ckZRTltAYvWVl8MMVTdzcfZz/B4GHgCez5rteK0BRt4i28I3qOE4YtMJFIXljGImSIgFwI97OAAAAAECAdAJr8V4ooQAAAABAgHQEL" +
        "q0yvDmtyhoqCLS0/mFnf5xoAAAAAECAdAY/r7sJXiihAAAAAECAdAgT+qZXkEFxGLx/b+rdwViaz592eLOFV0SHLZrNpX2touNVI3B0dBdSxtP" +
        "8nEZkQHKT3A+aA==");

    /// <summary>HM 18.0, 36x44, 4:2:0, 8 bits, 7 pictures, B slices that refer to up to 4 pictures, with wavefronts, rewritten by HevcTestStreamRewriter (SpsCandidate): its first picture is a long-term reference picture of every other picture. The pictures are not those of the encoder, and the stream has no hash.</summary>
    public static byte[] HmLongTermRewrittenB420 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAgIAAADAAC6lwJAAAAAAUIBAQQIAAADAAgIAAADAAC6oFIMd3ll6qyMX/l//ugDsBAoAAAAAUQBwZCeiEB0kAAAAA" +
        "EmAa8I2J3xNi0KUl/PTyLKnC/DJA1zkTzVTEoBngjXQbYf/+V6ufL1mshAfmZJthZ9zoB38RL/Mgfs3uyeLFRE0myIcYGsqt9mPDxLB2tC/1XV" +
        "qAtPCoC2OhLJhLX/7gFXS3vXKhHl+9f5RnY90Rs2TP/fFl2aPLMwomTeKZ2eJsxRrLrdgdid3ePExLnQPyzA0pdbSqSIcWmHvRBT6TeyL5jipR" +
        "B9+ch8XSix+Prw7/AZyntNIQgTP4yOh8BSxac05RNPIck2ekzrrXqG4PQkTA1JgzIuv2fSfQCGFd2et2ok9RBPbl1wsUR0QfWin4LP4PD0gtH8" +
        "S07oIPkKV4kr4JAIf9+M2YxjGMIHC8ZkrGk+YuBWuiU8DM8WFa3h7T0gZRDRqmW8RThMh6gH0FlwkvX1ZvaE54ak0BgeHIKj4pEN3IAYCl1lVg" +
        "StSJEF91pFMgVv55Ij2SvPSnKzrr6cld39TWT+IXE5X56AG5nH3PbrWZebYyEqzhkQlAxgT62ncxqhzbsDC3WlWVyEJu9AdWmOseXt/h7ozD8B" +
        "5TLoKZRMuzkmun/TPzdYHLtbkBhhav+jvEOuu13T92MlI3dQP6A0HKVYUALDUvicKgAGgAAFgzoofHGlIJhOq7xtVqU5ksWL+sfrHhUG8vZqBy" +
        "78htJJYPj20/jV9G2oRqzqNTxFE8FXp6BY5XG8svGDRs8VnyJXQLMNLEH3+fq2BZxaORlWl8HvnvPFnM0BAgdOb82+J55oE0T10PmrFDZHwelZ" +
        "9obDoLVJH1xIsAJb5OEEYoZBtgBpIw97SveCfCP9dDwrqbM3gTplXmEQ988CvzTyL7pRnwAAAAECAeAmr+EF2piUfYACDgQsAAAAAQIB4ELq9K" +
        "SC7UyUfYACDgQsAAAAAQIB4GP6vbkguzShQNqhfIMIk33yOvvIT/pcv69Tgfb+9bx+Cm5RE3FRD04hg6vHPP6Al4WAA0IAAAABAgHggT+rpILt" +
        "TJR9gAIOBCwAAAABAgHgoT+rpILtTJR9gAIOBCwAAAABAgHgwT+rpILs1QW7c+48zpV/9nzAOj+96eC5zOO8qIQHqLPJeUpAR8gP8EtE7Axhfv" +
        "1OyRjgmTxAA2Y=");

    /// <summary>HM 18.0, 16x16, 4:2:0, 8 bits: --PCMEnabledFlag=1 --PCMLog2MaxSize=5 --PCMLog2MinSize=3 --QP=4.</summary>
    public static byte[] HmPcm420 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqgiEWX5JG3d7PbIAAAAAFEAcGQlYESAAABJgGvBbiSYChTWV" +
        "VTDUJNMiYzJWuOkahMSj82WFBcS0sqISlaU66fS0cDHiIuIx0MBQIfPR5INFhKFTshDRMaFSIOJAojQz5WUh4TIxwjGhglCA9HDhs/XTcyGRYD" +
        "EB04ExkmOEMkKUhqHBQfR56CaH8XNDEtdqJiSTAcAIySHyeorAs5f6YxZD8fGK5tOj8KGI+NnYQ2PTlYLS6SgiwxFxuef4COQUpnVw08sJlAh4" +
        "kYk6SljDp7Pj8cA5KURnaAJJyEl4M5k01SHxZ0kUJCGBqTjHeMJURERB0ujJFXTgQlmpKjqTsqRVUxFEF0iEw4qactJ3KUSVFhLBQ3G4mGhKIT" +
        "KBsrk6XY4N7u6uGrmPGIkIGUnn+RzZ6qkZ2coY7YlIJRV4+FPT6BK2ilRR6EYIY6LlEcLTxKkkZZjQAtg1RVgEtGko0wvKWnnW1u2RG9f5WSeY" +
        "S5UaV4fntzjoVsppS0ksaBkrTxncJrd8zHXfqUu6WyyquO76jJg3u5vHvfkHSkqI2Aqv6AAAABUAGEMQAfPPiDijgWwa3HQLBn3wb4xkQQwbT1" +
        "k0qzYHHm4KpldkNi1BpJIeOlL5dMqQUUfwSA");

    /// <summary>HM 18.0, 54x62, 4:0:0, 10 bits, 6 pictures in a random access structure of 4 pictures with an IDR picture every 8 pictures, in tiles: --QP=47 --MaxNumMergeCand=1 --Log2ParallelMergeLevel=4 --MaxDeltaQP=3 --LoopFilterDisable=1.</summary>
    public static byte[] HmRandomAccess400TenBits => Convert.FromBase64String(
        "AAAAAUABDAT//wQIAAADAAgIAAADAAC6AAC8ilcCQAAAAAFCAQQECAAAAwAICAAAAwAAugAAwcgQd25byKV7pthTkkmlJU3k6iLUnKTLv17ZAA" +
        "AAAUQBwaVvCStGQAAAASYBrgqkJW7A6zmLb4Kr/RH/ybjmHyn92NDCDKgtQMsAKews3T0juuWSkffPLQGA5mc+HaFrFOBd0kDP3HedDpwYAAAB" +
        "UAGEEQBckh7tRaxxyqGxKZ3YW+H3gAAAAAECAeCU/CgukIkr5UgICcDlkg363G2yx4kg5vwv58CgniYUO018N/sp4AAAAVABhBEALILrAlwCl8" +
        "FAeS0fTUGj84AAAAABBALgVYJQWESsjoBLQI6AS8AAAAFQAoQRACr267ItwsGMwB8owioOjHiAAAAAAQYD4DaCUGBErJWAU8CVgFRAAAABUAOE" +
        "EQDzkzzvxjXL+fg/sPH2syj+gAAAAAEAA+B3glBcRKyPgEzAj4BNQAAAAVADhBEAm6NF1PbX+W/F4RXJXqJn1IAAAAABBgPgo+TJKDAiVpWAU8" +
        "CVgFRAAAABUAOEEQAsgusCXAKXwUB5LR9NQaPzgA==");

    /// <summary>HM 18.0, 24x34, 4:2:0, 12 bits, 8 pictures in a random access structure of 8 pictures, explicit residual DPCM in the blocks of inter coding units: --QP=36 --ExplicitResidualDPCM=1.</summary>
    public static byte[] HmExplicitRdpcm420TwelveBits => Convert.FromBase64String(
        "AAAAAUABDAb//wQIAAADAAgIAAADAAC6AAC8ilcxIEgAAAABQgEGBAgAAAMACAgAAAMAALoAAKDIKfIUpbyKVzE1bbBJxERNEiJJtRFSTJiOpI" +
        "j1EckRKSIqI8kR1FWpIjlEYnL39f2HL+vtsBIIAAAAAUQBwaVaCkgAAAEmAa8KQP3Orc59vegH6d8EmgjOceqBJ8Nd0MSN64KBc+GSFiuFMqQi" +
        "mJhDNg1y4iIn7wC+7W23D/0eekp6EWzdfId1QGFxGAmyU5f88vTh0Cq8edhp6sjhK9QQzf5LYAG9a654cMgGct8qBiWJdjucKREKvAqHYfXA8x" +
        "WRSh77OpH7OjG343kTFbHNI1Dl8mTszkTKqjrKbz8E7YQiwp8UBwDYFkuT4lcgHGoonQfEx8IJmynwAAABJgEt4UjzX1f3+KdxmuE0PbI0jTab" +
        "qJWV05MKV0vI3RQmUM+AAAABUAGEMQAUbMocn2LWOtrbdfMqu/AnThm34lwIETEUg/PZSFrUivl+C2zMsU6zrwRglZttE5qAAAAAAQQC4IiRJP" +
        "IYgOULfG6dlIlpCvcAjTTuNgAAAQQCXBESJJ5DEMMYAAABUAKEMQAVysmtygj5LZmEJ5yiOdJn/1/lavGMqkfWgwA5RFYfXuLU/hWSzd4mxJMP" +
        "tOq3x9WAAAAAAQQD4EiCmFhaDAdQAAABBANcCRBTCwtAzGgAAAFQA4QxABRsyhyfYtY62tt18yq78CdOGbfiXAgRMRSD89lIWtSK+X4LbMyxTr" +
        "OvBGCVm20TmoAAAAABBgTgKdzCwpCjEAAAAQYEXAU7mFhSzEQAAAFQBIQxABRsyhyfYtY62tt18yq78CdOGbfiXAgRMRSD89lIWtSK+X4LbMyx" +
        "TrOvBGCVm20TmoAAAAABAATgaauYWFLZZYAAAAEABFwNNXMLCkDMRAAAAVAEhDEAFGzKHJ9i1jra23XzKrvwJ04Zt+JcCBExFIPz2Uha1Ir5fg" +
        "tszLFOs68EYJWbbROagAAAAAEEA+DJbDCNhaCbkAAAAQQDXBkthhGwtMxoAAABUAOEMQAVysmtygj5LZmEJ5yiOdJn/1/lavGMqkfWgwA5RFYf" +
        "XuLU/hWSzd4mxJMPtOq3x9WAAAAAAQYE4Kkcw8NQmFAAAAEGBFwVI5h4asU4AAABUASEMQAVysmtygj5LZmEJ5yiOdJn/1/lavGMqkfWgwA5RF" +
        "YfXuLU/hWSzd4mxJMPtOq3x9WAAAAAAQAE4OuuY2GIl7AAAAEABFwddcxsMcVoAAABUASEMQAVysmtygj5LZmEJ5yiOdJn/1/lavGMqkfWgwA5" +
        "RFYfXuLU/hWSzd4mxJMPtOq3x9WA");

    /// <summary>HM 18.0, 16x16, 4:4:4, 8 bits: --ImplicitResidualDPCM=1 --ResidualRotation=1 --SingleSignificanceMapContext=1 --GolombRiceParameterAdaptation=1 --CrossComponentPrediction=1 --MaxCUChromaQpAdjustmentDepth=1 --TransquantBypassEnable=1 --CostMode=mixed_lossless_lossy --QP=4 --ScalingList=1 --MaxDeltaQP=3 --MaxCuDQPDepth=1.</summary>
    public static byte[] HmRangeExtensions444 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqQEQiy/Vt2e2wHCgAAAAFEAcGQlrEiwHUYM4AAAAEmAa8FvE" +
        "zg4+5fcD+H/J/x/t9OI9FoBda5gSLlvvZUZvbZdZ5Nx6VXRQt1T/ZDU5+p/JRSNRe9Se1l9zMLCbb6xoa5zL3C/mt3k3QQdPCJqC8di0ylY8zC" +
        "8qYawJBcL+tgfUC5pDUIS4gf/l+F+hEuUwl7MLmCFrFWNpYheNByBwgCA60RpRS/gfKFxKxy3OrQBsv/IP/9DeJe/gFIga+r8SGjCIdJdaan2j" +
        "y6E84SxTGmN5fF2iQgbilIamb4dFRWeKAQ4wwfKHFNac7daizia2vJMQ2slgUB/7/4/SjCW2cXtSX1jkxA5FsX1gmSII70hu+gXTGA//9W3KVA" +
        "pUyvdeR1SkqC1uhHSofy/A0/HL8+4LxwYYYc0A/UD2CL7a8dnAVhU1w2Ompw2cragkpqUSerS2qQf6PgPs5yQsQx7IjQxJfUVLLZP4A9vVQUdo" +
        "obuaoXCStS8Cb9U/ajAe4lKWDnErviwO5RjWO9Cf+ZRCzTPsKwuldZuuJ6wavtaB54GbpqlSmFXsfSwHFE4b/kfX1nmkzDHVk9V0QVADDD/BZT" +
        "U5bgdpeGBMPu3LjPxvHyOZNJNMBQ1dSmKOl3105VHRN2yt+q9/FTSuVlsGMUgVbY+2ivaEHWskfn6frk/5T4cHPvgdqjdaosyeqnmuAW00Z0Z3" +
        "h44SJlU2LqzgGJGQqv0+q5CotNmJ1HJsDiRObj7d9R5GCGn6C/hTRMr/3x9hQQ2ulJh+c1bQ1mgAGRDFAQQxLVocRB84NgRFXwOfrs9hDnu8tJ" +
        "iMe6RAytck9Q9PdRu30yUrtf/O62Tkgp1d2L2RTN5YtI2lGgihk0ahq0cQeaY8021ze6zSQI0bzbvJFcYGXZ4wPcyyzF54DpDTbp9z14VZ2P7l" +
        "hhWZtFr9l1TuK84KHiLIwgvUYlD29pEdqDiWEmw7TdFS8EbknYAAABUAGEMQAfPPiDijgWwa3HQLBn3wb4e8jgnODLCjms8Dza1Cs4ZAmkiBFg" +
        "xa3csZ4pDI4jWmiA");

    /// <summary>HM 18.0, 16x11 (a conformance window in a 32x32 picture), 4:2:2, 10 bits, band and edge offsets without deblocking: --QP=14 --LoopFilterDisable=1 --ScalingList=1 --QuadtreeTULog2MaxSize=3 --ResidualRotation=1 --SingleSignificanceMapContext=1 --GolombRiceParameterAdaptation=1.</summary>
    public static byte[] HmSao422TenBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqwQghxMLNl9qns9tgMFAAAAAFEAcGQlYGkgAAAASYBrwzg+Q" +
        "LYItk8mMA8p2myc4Hu4kUvsUFyADxJrZXuNLYLBtKAEKtda+m30Upbbz+C7ePwx30uIki1DNg9xJ3seHpbYFhJOI1vri/rJw44owux8OF9SpCt" +
        "l6W5HidWkgkMofsa2nAdHYGeYJr0vzVtqlkUAAZgyoJOTG2ISg/8gZgd/NxaIft2YtMiV2YmVs34HI9qnaLXy4rTtLgj9egcvG+1h0t+ucUPGn" +
        "cFzDi/uCP03A+Atyq+CPmWuzpwnSoTJB9yfQrRH8CymUZ0K6qD2redtApwUZ/Uk9NACCakxoJTFgKcYAcC6ATWp3/+z54dSAEAIJkb2dut86zB" +
        "Thcb5RJ4750yfvT85R2k/hdS9HzL26DKunoKbiPG3Zn5PULkaTUxGVnK02MuZs3AD/VCdtYFlSaQMh+8yIM2ynfiaM3MXftkZc23+AAAAVABhD" +
        "EAbbbHytPp8ZBggBAKF5I/k6p8FlBw0+bVC2vp5403ow9PAtp3ylPlrigFWUEDkn2LgA==");

    /// <summary>HM 18.0, 48x32, 4:2:0, 8 bits, coding tree blocks of 16 samples: --SliceMode=1 --SliceArgument=3 --SliceSegmentMode=1 --SliceSegmentArgument=2 --LFCrossSliceBoundaryFlag=0 --QP=40.</summary>
    public static byte[] HmSlices420 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqgYghZfq22e2QAAAABRAHhkJWAEgAAASYBrw5A/eU3QmvdjV" +
        "gxAfjHpzX2revmvYPmAd2vSjS6Cwo190UbTGEAObp04zscfgpN1emqWR8qojeZ4lDnwtAILa2zohxk8ZYtIV/J0/U5il+IRt/3L5qbfqzsa9Sf" +
        "Mgm7FjNWUe2FZgcwIYHv47HfBKlMsCxhA6DpyYxzDlbuauRtm0GDgoAAAAEmATWvjCFWDvzXnJK4Hge0xbma3n/tFGWfy/4r9xRQMB5Fn/Usic" +
        "KOgX0KBQXZB2hP+h7fhsV57SLHlVqWAAABJgEm8OT899ewiJSCG/EGAMhMa00+M4fXMllUsOAAAAEmATuq8TtW0pQM8AAAAVABhDEA1o6XJMvT" +
        "U5w0HGhQsdNRcEoQ22Z5w+A97joDZ+B4Z40o4DjYThKnl4mx4ixubHcbgA==");

    /// <summary>HM 18.0, 48x32, 4:2:0, 8 bits, coding tree blocks of 16 samples: --NumTileColumnsMinus1=1 --NumTileRowsMinus1=1 --LFCrossTileBoundaryFlag=0 --QP=40.</summary>
    public static byte[] HmTiles420 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqgYghZfq22e2QAAAABRAHBkJWElRIAAAEmAa8OSCEaCD795T" +
        "dCa92NWDEB+MenNfat6+a9g+YB3a9KNLoLCjX3RRtMYQA5unTjOxx+Ck3V6apZHyqiN5niUOfC0AgtrbOiHGTxli0wwP06E0yQCCDUXWsjvjiS" +
        "gWDNXkcaUMZZR0wrtIhs1lj2pc/+ecRd99pPReJXbgmE8ggnxeX+GuVGgJ/yNXp/v4Kj0ta1+q8Pm9GY/f0mHCbYD2w0bKD9kDJt1VNXlzZPPd" +
        "AAYUKQ936WejNxCI7fuGjqJ82Ux3cwVJUwWrAreu9tn0XA/PfXsIiUghvxBgDITGtSkPvar3CiS4U2vdZq3jSEF28zAAABUAGEMQCY2xO2O73v" +
        "IzF4LVprMr5RtiQj0b5UvGZMD/5+7dQBX/YPufbeQvSD0lq7JOJDCj2A");

    /// <summary>HM 18.0, 24x24, 4:2:2, 10 bits, 4 pictures of a fade in a random access structure of 8 pictures, explicit weighted prediction: --QP=43 --WeightedPredP=1 --WeightedPredB=1.</summary>
    public static byte[] HmWeighted422TenBits => Convert.FromBase64String(
        "AAAAAUABDAb//wQIAAADAAgIAAADAAC6AAC8ilcxIEgAAAABQgEGBAgAAAMACAgAAAMAALoAALDIZNlvIpXMTVNMEnERE0SIkm1EVJMmI6kiPU" +
        "RyREpIiojyRHUVakiOURicvf1/Ycv6+2QAAAABRAHhpVkRIAAAASYBrwRY+yCDFMO5gBs/ORl58iUtkQBAcXyKrIAAAAEmATbSr2PIETfg1Ycl" +
        "clCu+XKSqFlpWAAAASYBOtolcMRRDAAAASYBPs/6Lx97ywlxwAAAAVABhAcBxQSq1uS8gAAAAAEEA+BIgofw/GobG8ahsbgiwGNAAAABBANsUc" +
        "AAAAEEA3RawAAAAQQDfGDAAAABUAOEBwEuv6QwdYOAAAAAAQYE4CnYYE9RxR0docUdHYKMbcAAAAEGBGyNgAAAAQYEdJaAAAABBgR8z4AAAAFQ" +
        "BIQHAdyk5mcIlIAAAAABAATgaasMSf48g+PhsQG4b+PIPj4bEBuG8E2Aa0AAAAEABGyMgAAAAQAEdJWAAAABAAR8zoAAAAFQBIQHATxWaiP01I" +
        "A=");

    /// <summary>HM 18.0, 48x32, 4:2:0, 8 bits, coding tree blocks of 16 samples: --WaveFrontSynchro=1 --QP=40.</summary>
    public static byte[] HmWavefront420 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAAAoAAADAAC68CQAAAABQgEBBAgAAAMAACgAAAMAALqgYghZfq22e2QAAAABRAHBkJWDEgAAASYBrw5Qi6j95TdCa9" +
        "2NWDEB+MenNfat6+a9g+YB3a9KNLoLCjX3RRtMYQA5unTjOxx+Ck3V6apZHyqiN5niUOfC0AgtrbOiHGTxli0hX8nT9TmKX4hG3/cvmpt+rOxr" +
        "1J8yCbsWM1ZR7YVmBzAhge/jsd8EqUywLGEDoOnJjHMOVu5q5G2bQYM8nBnnD1bXnJK4Hge0xbma3n/tFGWfy/4r9xRQMB5Fn/UsicKOgX0KBQ" +
        "XZB2hP+h7fhsV57SLHlVqSwUF622xiQ/hLWMoDazv5q6exUKD5U9GEMNdAE4AAAAFQAYQxAPwLg7oqkaiD6KC4YIlebtvJn4kl0JYJFi6hFDB5" +
        "gRW8WxDIMlsKJ/ZfBjE2/aJA54A=");

    /// <summary>kvazaar 2.3.2, 172x134, 4:2:0, 8 bits, 5 pictures, P slices in three tiles: --qp 41 -p 16 --gop lp-g4d3t1 --ref 1 --no-bipred --no-tmvp --smp --tiles 1x3 --sao off --signhide --transform-skip --rd 1.</summary>
    public static byte[] KvazaarInterTiles420 => Convert.FromBase64String(
        "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" +
        "C+4gp43MF20JaGp5Ew1zhLkI6kPEq4A=");

    /// <summary>kvazaar 2.3.2, 144x138, 4:0:0, 8 bits, 6 pictures, B slices with wavefronts: --qp 43 -p 4 --gop 0 --ref 2 --bipred --open-gop --wpp --sao off --signhide --transform-skip --scaling-list default.</summary>
    public static byte[] KvazaarInterWavefront400 => Convert.FromBase64String(
        "AAAAAUABDAL//wFgAAADAIAAAAMAAAMAugAAPAkAAAABQgECAWAAAAMAgAAAAwAAAwC6AADASICR8nPkkuFQCAAAAwAIAAADAMhAAAAAAUQBwW" +
        "CJYKZIAAABJgGuxMHF0Pxjp9xobqwu8WkI4JvaGCS+SeQKvVi5oRFkI6fXypsRSkcrp0qyiqe7yqgfJOYbRU/Ob/i1yaq7LrH988ii6k/IWVWb" +
        "vflBB6mbQhXVFy/v6CuxeGnd+AdzNbCORTQMtt9zcvccC1O4Z4/eiZcmRxg7LIDTy+1EJhC8LleDuUf7YcQlqT06N2FStNrnroaS9POAoolQ6U" +
        "XgWBUv590L06sQzq1YLFdO09Silg/BheH/e0ikgggE1UYjpaXKGpOZqGZQFH0FjxUeQwcgfxS/b3rn3JbZxENjkCE5Ma9YCmisKt8oZQSi5m1/" +
        "hEmMbHDu+rcgskEl/QANagiWrL+4zDbOUtYIMUVwQN/UHzVArFVYXFdh11HC32VxfU9eVr77axoCQaBPwhrjBBY1dmVEOtXFajKuvKewY0b0pL" +
        "YwAAABUAGEEQBGpiaPUs4pIenPdf4Rbht2gAAAAAECAeJf3Zsh4FuDAY/Fxg5UtR4h+6X7LhFKZW3nHmA3cqCj/rgqd/P4VAbT15JKvWEKwKNY" +
        "pXH0ywzicFQO4XTOkgwoDT8MAAABUAGEEQC3OdgX3FF8BnDAvYfYaATQgAAAAAECAeR/0ns8ErD1KdPSV39K9aSqoVwKyj1G66pKYA5eRB+NX9" +
        "2TjkAOeabrUWFh5nX8Ki2/J4kvPbkNM5m9xSr4hZMl1EhdMUtYZqysRb50Yn7/ozEhl++uuIcK7wcS1fa3NYixL9T3NI4E0AAAAVABhBEAfvSP" +
        "G4KpJJQCYjd6zdx0E4AAAAABAgHmf9J7N2NA7MU1jbqRuSFbNtY0B9NP/X8AuaSgm0wWRiZYC3Mh71KcXbuS29TCI8gx1DlBnSorvL0U/4nEDS" +
        "LLOpiA0VXmufY9WDYd+brgAAABUAGEEQA7jaeuB22Iyk46inhOFwWGgAAAAAEmAa7EwstQ/GWn3GhurC7xYAncAq+CI799puYcadijS3cOn19F" +
        "D5vvIacKnZYFbEmvMuqgep81tAhc3Gj1GSSR7ZkRub/bhiNR2lPUiUgkFHC/V8BgF0FJcQiJyTM03eWGqVZYA+l37qa1NqYC5qQC943CraVNih" +
        "MxuYr7PcLb2FuLQzGZpR5Swh7bGP6W/QK84H6D2bhMYGTQUlK8wU1wRXv4McqsIttvxSr8QYJl/GbwAAtHB6axbDcl/L+pRXj7Dsrua771Xm+u" +
        "jAqx1KmNERYCmwKpgXOBwGuHxGuMttA7yobJKwbzchL/0pEJtgOZup4aGP1HOMhICcx612R3+t5HjK3Et0OVuvrhuV+eyhX9iJlHhtkVBiPtZk" +
        "2ug8mECfRK0TUEpKDKISHjlsGSoulBjCu3Xx4ikv2mwopZbt7wLL2aaX8PdEWNxGSHWZOrM6XYvRUsl/KwDWtRRx0vDryiz5ROts5kNf1wbylh" +
        "AADUgAAAAVABhBEAk+0qRgB3cyQ2utIMTuHgjIAAAAABAgHiX92e4LjtvMzdgeUTB0It26o0JkrJMB31MbYB7fwWXH+PEIMpnUQdWUst8wWmvj" +
        "DIY6GqkaOlKdAMBHCSIZR8DwQoWd33FlHxpCbX08hQbZq1nzOx9EMSbZS1u9y8gatIQJiXQO3W/ygwBVTpToAciGdYAAABUAGEEQCdJxwtjr1X" +
        "9OWv9h5gP0JAgA==");

    /// <summary>kvazaar 2.3.2, 194x146 (a conformance window in a 200x152 picture), 4:2:0, 8 bits, two pictures, two tiles that are each a slice, with wavefronts in each tile and checksum hashes: --qp 25 -p 1 --tiles 2x1 --wpp --slices tiles --sao off --signhide --vaq 5 --rd 2 --hash checksum.</summary>
    public static byte[] KvazaarTilesWavefront420 => Convert.FromBase64String(
        "AAAAAUABDAL//wFgAAADAIAAAAMAAAMAugAAFwJAAAAAAUIBAgFgAAADAIAAAAMAAAMAugAAoBkgJnJJxeSTCIBAAAADAEAAAAZCAAAAAUQBwX" +
        "PDWTJAAAABJgGtsmnAyLH/88boKqQ/Zr7tf9ATsDMgCAT7BN2msIAAAAMAAmYAAAEmASTbLIvIsf/zxugqpD9mv0sVRVNAAgvgEh1gAAHtAAAD" +
        "AAAMaAAAAVABhA0CADYToAAVbAgACCaIgAAAAAEmAa2yacDIsf/zxugqpD9mvu1/0BOwMyAIBPsE3aawgAAAAwACZgAAASYBJNssi8ix//PG6C" +
        "qkP2a/SxVFU0ACC+ASHWAAAe0AAAMAAAxoAAABUAGEDQIANhOgABVsCAAIJoiA");

    /// <summary>x265 4.2, 24x16, 4:2:0, 8 bits: keyint=1:ctu=16:qp=32 (deblocking and sample adaptive offset).</summary>
    public static byte[] X265Main420 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAJ+oAAADAAAeugJAAAAAAUIBAQQIAAADAJ+oAAADAAAeoMhFluqvK8BaAgAAAwACAAADAAIQAAAAAUQBwXGBEgAAAS" +
        "gBrzbqbSj0ee3Vb0G83bgaJeIjM1/hDiOz7ZWMeO0+E8ObIzIrxysgM63mmyja+uhr1O8gDG0mObD3afLx6kyJvF4mvUhiAxpBi+lEwgZdram1" +
        "Lf3qiB/W9pLgkzBiAZ9uOV/A63GSW6pGbDJDKlBs///POeu6N7v+cgTpy1+FTip8kpoD5fDQUIBzWIJ1C+zRX/sW0WlGF46ytJFql9wQ1EBI4K" +
        "yLMzFYWF9lb0NaAHI5P7A3f98insu1HbGLZhHSZ7j3F/FrUi+teF2Vk+kzhDL9LkTH31KF5SIiZJjBlOEBt/N667H6JYZlSfBgxpImNvrojQLX" +
        "f/tBPw5qYDQ1vzmy3yPefAAAAVABhDEAGoHP9I3V45IE7qlgMrlBLTDmGewPHVZfhcr4vSH9KyMVBDvB5OH/C6sWqGN5kAhDgA==");

    /// <summary>x265 4.2, 19x17 (a conformance window in a 24x24 picture), 4:4:4, 10 bits: keyint=1:ctu=16:qp=24:tskip=1.</summary>
    public static byte[] X265Rext444TenBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAJwoAAADAAAeugJAAAAAAUIBAQQIAAADAJwoAAADAAAekBkM5ohst1V5XgLQEAAAAwAQAAADABCAAAAAAUQBwXQwYB" +
        "EgAAABKAGvF4DX3j6JT4dIZYpeGhOEUUcfU63ts4FxLLSYc1e2pa1Ug2uHo5FcdR4TlrK+PdGGRkop7K0ir9nCX6lm66c9Lrnrv3Ix7OjjU4FX" +
        "O4IhQxayMiTAOQbK3rEKti4dVWR3p1jRpS9elhXrDnC7mhmuriZCXhoMfywV0K5TyUR2OjPtz1BdShil+/YoHLzljR8/QMsTaVwH8+YWjkIvz9" +
        "9zDFagw180QdTrlvesyQkbPnkQMx+IBLOXoSQw3PEfQEpirS0AlBDMYXRm8mRMBd9YWn9ZwkCI+Y6zHOqzcJBW44K83+7ufTCEAH1HxjtSQR+p" +
        "vsqB75SVCIeToeSaSrv1Z+NOpk5WozJAhSDXDOT6aDysLJe87+F5oXQJUCNIqasy0SbTebeOPZWveFd8Kz837q5sQ72KWJAbONXdTKs/13Q7zg" +
        "20v0e+1iV24oZIYXnPsO49HuIi6ygHmp5JXUdWd96pnY6u6uqpzbgNjBCtMkRVEci2ykMngCj8+qyhT93LlUacJlH5mpyVK7ez1rh4YDZUhhSU" +
        "kDjYi5I6bwt64UrGAFaYTGci/eu6jYfdkZQ9UFBmHEMmWcf0XJbrvDWEI+6mbcPv9qChDz/hh+qPODiE/kxWGeR0wMG7GF6Vy+T8EmWn8Z+fCf" +
        "O/tKf9x2zPQNN8LMq/FYiUQGnxb6WWmZqw+PpdNwAAAVABhDEAs2U8vvnICuHFR7D1JuRJKWHEfFIot+pScyvZX1CzQBWaCRzfgkOfggUxGbkt" +
        "pLZogA==");

    /// <summary>x265 4.2, 148x84, 4:2:2, 10 bits, 4 pictures, B slices out of output order: keyint=4:bframes=2:b-adapt=0:ref=3:qp=45:aq-mode=3:qg-size=64:weightp=1:weightb=1:rect=1:me=umh:subme=4:open-gop=1:min-cu-size=16:tu-inter-depth=4:signhide=0, checksum hashes.</summary>
    public static byte[] X265B422TenBits => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAJ0IAAADAAAekRAJAAAAAUIBAQQIAAADAJ0IAAADAAAesBQgYc8a2WRESkIQrwFoCAAAAwAIAAADAMhAAAAAAUQBwH" +
        "AxMYmSAAABKAGvBBj8xP313hmoTeJVbvg6p18b93z+xx1WgYdr0ynqbFdMiJJvVoSC78RUWzfk+nK10hXM5fjJC2K5RztkvXqMLf5sDm2RHKg5" +
        "Y8J36KYxe2l7eowYGhwKLPuB1+syn4M3u670Ej/N29m+qQTfcoZcG3K+4hVHSURZZBHrI166EE5MoMRgoRkDexCuqZSKRE7lNxnowo7b1Wc6L1" +
        "Kxk1BUsGS/O7fvlB9d7GjyUAWyw+AwwPcsQBz3IbN/nQjLR08mogsMoyJZW01l/Q58muEqq02U+Weq/mO0KBn2LzaXuy7hkATaPQxgBD9zMA6W" +
        "Ah9HjSxnwu7tgAMlwNAe/rnQ8+Qt5a/9H/EZISlCa7EcxvEHMbfpIw6RAjkplznilkaZRwyyHS9OamWhzLFesyWRzR0H49L56rAEXJo6zhgHpL" +
        "yHuurL//Or3WLUu/R0nc+CA/keIo/BbfGW87u7K2ARdqcAllVaqOfh1g9LhKREmMG1Ss2Xkv221Q78A/0wcBKCiz13cLzZZ6+iEn3jwewkp2Fc" +
        "En/UJe4oIeIKZAuKX+bKvchsAuX1uJippoPEmmBs/F1X/BjoF9mQ6zO9DblkTKMJMc/JIobZECiArzvZpKOrsHHbHYagoaSq4LWg4o4i4nOagL" +
        "kheYjkd/RbsnMykhNyXZvoFxYW++vDMUvQsbMxfidWXV3IapHCCtLyj7sWQiwzTPGBHqWxsEMlRVdFHmKtkN/tD4ciCXxTmkkltmj36VZ/MK9q" +
        "NtQG4gyjGMY9q1KSFCRmClPktvcOer9XW67a5MYrv1n/i0ygSgPYJ2gXpQeeZWWR3Lb8Iit9y+AAAAFQAYQNAgAvrgQAFlzGABY5HYAAAAABAg" +
        "HQGV+EMYJk/sn/kQ+5mhDz0bQ2cjZEww3Hvwfx/F39p8My1GaCr7F5z4rQxshXtbjApTmTCNnGiU2SZgfmZhjJbcleZd0lT1cJxeRjjE849cLX" +
        "FO35jRithrulxeJMY1JGgAAAAVABhA0CAC9ZhwAWUy4AFiFWgAAAAAEAAeAkteBDBgqwCmkSPl9YAKg2fAAAAVABhA0CAC+HxgAWV4UAFheEgA" +
        "AAAAEAAeBEl+BDBgqQABYR0AAAAVABhA0CAC9m2gAWWZwAFh+JgA==");

    /// <summary>x265 4.2, 24x48, 4:2:2, 8 bits, NOT CONFORMING: keyint=1:ctu=16:qp=49:cu-lossless=1:slices=2:pools=1:wpp=1. The data of the first slice segment do not end with the coding tree blocks of the picture, and the second slice segment has no data.</summary>
    public static byte[] X265BrokenSlices422 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAJ0oAAADAAAeugJAAAAAAUIBAQQIAAADAJ0oAAADAAAesMgxZbqryvAWgIAAAAMAgAAAAwCEAAAAAUQBwXmAEgAAAS" +
        "gBrwUQ/nDmXBPtmcqLefxUExY0PpFkOaMFHa8db26Pl85OT+ToGtueuFsNt/ttARSdkvsKYQhlKCaZaouBdiSDOGPLnmquqKSWLhQYsh9rIDf9" +
        "cKu8ox/PFbRVEnAAAAEoASngogAAAVABhDEAiaNLzqlwUJwFbQx5BYQCq+So4tBzhS/Tb4/HLoyJrziUoaHzL1suhyYBwEOAsjbDgA==");

    /// <summary>x265 4.2, 16x16, 4:2:0, 8 bits, two pictures, the second one in a P slice: keyint=10:bframes=0:ctu=16:qp=30.</summary>
    public static byte[] X265Inter => Convert.FromBase64String(
        "AAAAAUABDAH//wFgAAADAJAAAAMAAAMAHpKAkAAAAAFCAQEBYAAAAwCQAAADAAADAB6giEWWSqvK8BaAgAAAAwCAAAADAIQAAAABRAHBcaESAA" +
        "ABKAGvWKRl9ERdTTpbHR/yEzdGMCKtyt4NpjMWg++XoKcDuGA14JSxN7hOtpA2Tlf1Z5ou4+yYO01aWtUwvJk/KlICcyPy5BSNX5+6hOtwf/pX" +
        "C0mbBEjmzW6o0tez+3w7UGlyYe6jRrfAED8DhVKxfcxWnKQ2ufYasSHAuIxajy3AwNFgNGjgUn8l1we+5h50qn/vttYwMtn5WA8upAHwnp+5Iq" +
        "nH0XgAAAFQAYQxACsogIS+tVOFuX3qcerxXqPlj7ucYjYs/oi/3JYTPsAvnYlZSSIHpF9hlwwUqORZPYAAAAABAgHQCX4QxiMr1xzA/srUUvC6" +
        "zhJwDgR0zqc0q8y///2GQXI1yWfVr9uI1PXeYitHSC8q02G0pskyPvgAAAFQAYQxAIRgEA/oKHF6xqSad0RL4LFATAhxBv2lIdggyFGGvuEx3k" +
        "4dk7G6CH8R7r/besMu+YA=");

    /// <summary>x265 4.2, 16x16, 4:2:2, 8 bits: keyint=1:ctu=16:lossless=1.</summary>
    public static byte[] X265Lossless422 => Convert.FromBase64String(
        "AAAAAUABDAH//wQIAAADAJ0oAAADAAD/ugJAAAAAAUIBAQQIAAADAJ0oAAADAAD/sIhFluqvK8BaAgAAAwACAAADAAIQAAAAAUQBwXGJEgAAAS" +
        "gBrwW4JJ0CrV//+AIv2nzKL7oqoqKj8fHx8fHx8MfX/G490P6tQSqU9LSSOC9U+Aasc7txaQa2YBLWUg72////LCazfV6k9V5/3//9hK4AvFN5" +
        "QqcEaNWp9bVtW7ABmhmhma2n//bqI8xm0zMt0TTn4vjuuCbd8fv4QA3SZjRCTij78Vm3btK6/RMwHDF2NmBgagsdA9ADarZ6lD//Vkv7a2EsVO" +
        "X5KhzgKm/R3zvpXvv5ADn4fkA8A5+HuZ1KmcI8YTSNcZjtPvn4Arr//9/jo19hdPdFwZOEMfQnh4cjOjCpLSS89/lbFnCkoQApE+M5ZX3RwDPz" +
        "zKAr+52/tg88sWcgdAxa7lUOscZ0Yusfq66ojeyhMJqk4WwReSz4DVADK0v2htk7bifkYC5CdIpBJ278w2CaLVy6nPOFLSFqNpnlb4Jc8irUL+" +
        "JUuC/ai9muKOEPHW5uldY7AhEp4bp9s//8Kuks2jtA++6th8H7o0VDixSLAWfHs4il3TldN78gt4nquSiZSZiMQQDZ1YNODC0unquLlwaQ7dGK" +
        "t60NMfG6lOA6kyHkhhJxOlNZihWjcvF7qsrp/6cf9KlHDjZqpPouL7C9woNEvPXotXYRwxL4lqtA5K9l3yCOjrbNh1vT8vijWH5ce6HnKl6avU" +
        "HDnJQUsL5JU8knufnB4QQNqvzPmLLwYuBhsNlE4TZzc1WRgF/FjeHE+vUIs4AAAAFQAYQxAEA3nzrNERaRphc2fpAUwf8e1Sa2RrGVneKtajWI" +
        "KlE52eMnDsYyGRa2n1iFmFquD4A=");

    /// <summary>x265 4.2, 150x102, 4:2:0, 8 bits, 8 pictures of a fade, P slices with up to 6 reference pictures and weighted prediction: keyint=250:bframes=0:ref=6:crf=41:weightp=1:max-merge=1:rect=1:amp=1:min-cu-size=8:max-tu-size=16:tu-inter-depth=3:cbqpoffs=1:crqpoffs=2:deblock=-3,6:rd=4, checksum hashes.</summary>
    public static byte[] X265P420 => Convert.FromBase64String(
        "AAAAAUABDAH//wFgAAADAJAAAAMAAAMAHp6AkAAAAAFCAQEBYAAAAwCQAAADAAADAB6gEyBp1WWepLZGvAWgIAAAAwAgAAADAyEAAAABRAHBcp" +
        "EYwYckAAABKAGvBRgWDDq1Sv0JAAFwAXVm/6IDGAAFIACWAd8AAAFQAYQNAgATxiAACixoAAosaIAAAAABAgHQCX7ggA/Sgoz+cO/vURcClvE+" +
        "/k1vxocx8by85eLrIBs8Kxugp62J4hrvQRfx3fFK5hrWcRwPOdy06KPKriAAAAFQAYQNAgAT0LEACT14AAhskoAAAAABAgHQEf/UyoHwfA6QPE" +
        "oKEBc+KrLaWRaRMXTAF1OCxESy7ll3CkgSZtyPQkbVYarql8HXcAWEPb5eB0wNrkFFsKv3L6POAgASHrcrAmwY5f2xDfFdw5kIRg8oO/sRIAAA" +
        "AVABhA0CABQqogAIhE0AB/l3gAAAAAECAdAYn/3MpARBMCFAjSgoQP5ldnncfIUu5jzUa7nu/7histB6tY18iBfQAAABUAGEDQIAFDjOAAiFNg" +
        "AICX2AAAAAAQIB0CC//ySkiBQsE0FEoKP+b0HNghvgj07k47kL//MWAvLGX9cvAx2t4cv7AnzSJP7r08SqGccEdEgAAGBPwwzOgNNvU+7oLES4" +
        "mQimc/oqw+Zg/dsKSEa8Dpsg6Vml/v7fy3FAObQ7lTDmA26JSmBDQekAAAFQAYQNAgAT0EgACDs+AAd0CoAAAAABAgHQKN//yz4QBELD0PSgof" +
        "6NZzXDJXVFR3R0ZsdBei0QponUAxSRGuwOZA9s8/3pOuiCs/9bC9GzcSfUMpM8+dNzh2JqfiQHAVe0pLmYfAzDz+Wtblrz7Zav8HYSu3tEfP3o" +
        "YJpXnE6r+UCo8FuIqi+trqHy8swAAAFQAYQNAgAUC/UACGeRAAddrYAAAAABAgHQMP//80+CANHDUNSgo/5lkFR3LfRIytrzsNtCU/pQ6OWAYF" +
        "gAAAFQAYQNAgATyzsACGQOAAdWnYAAAAABAgHQOP//80yggFHCkKSgof5vQYLPkz+6Y9a4ScNizO6UOZ/fs8wzOjnWnD9OzWusP8OBo2QPddtb" +
        "HeUm7DVQDSZUf/z82KeE2S7DCkME9tE2kS0qAAABUAGEDQIAFB8YAAhgXQAHWdSA");

    /// <summary>x265 4.2, 32x32, 4:2:0, 8 bits, 8 pictures: an IDR picture, two IDR pictures that are each followed by two RADL pictures, and a trailing picture: keyint=3:min-keyint=3:bframes=4:b-adapt=0:ref=2:qp=36:open-gop=0:radl=2.</summary>
    public static byte[] X265Radl420 => Convert.FromBase64String(
        "AAAAAUABDAH//wFgAAADAJAAAAMAAAMAHpEYCQAAAAFCAQEBYAAAAwCQAAADAAADAB6gQghZZEaryvAWgIAAAAMAgAAADIQAAAABRAHBcaESAA" +
        "ABKAGvHYD8p24y/zv6/4kUsKtCt7wB4b3iAMAJEABQ8ibM8GT1EMA2qJTLLKsq5qrjQYyavZcw28L/kqqKHx5VBqxAUQ2MQm8pfOE4T9iwlOuX" +
        "Ev8XXc0rB8DOE/zJSdjzn2790AWmpLIn42o50tB1V5DpoqTL7qRTF2O4X/zedLalIYH+/KygxKcP2TTV0l+cEYtG+66tDxdhQ1569ucnv+3cVM" +
        "26CRp+AEjPmQriRy4O+ZZnQ2gdn/da3SARKPX11cR8GLShHNRI+fvn78v14P8wfOSFbEYlT9NK2MXvp2p1lNuhfTde3EezU74Ejp++AfaySwDU" +
        "QyGsU9Y4tIhnpThMbg932snGpmoaXOGQUBcbg1DgAAABUAGEMQBmJO2vA4flWFV2s7I+ZcoRgE62juSbTUwBTwvLnHOikyP2tQ9nORPYjL+ivR" +
        "Uu8jOAAAAAAUABDAH//wFgAAADAJAAAAMAAAMAHpEYCQAAAAFCAQEBYAAAAwCQAAADAAADAB6gQghZZEaryvAWgIAAAAMAgAAADIQAAAABRAHB" +
        "caESAAABJgGvHIDCRPGwitxQw45qUdfjpI9FLg3/VeulxebEJddhonqESERisW8y/hWAn1Dja6ewi7LJytDkerrOXE+xnRwfgCmx5xV5eqWKSQ" +
        "chF1ED1xeR8f/95+I3T8CZop3w3jd+thERdNQODHTNnpbPdo6v97yGYiTmT27xS/ZQyV3mz7rhNIozi8nJVI3vfdSX3px5YzwGj0s9v2JCyuYE" +
        "idh5mu6eA1OKur2z4t/eaAVAVhRVrxt23MF05Tta8GGyiSBwQL/s7ts4k5HjOJnYv4ll6r1um39MbUg/4yiw7GuPk8DioxD3VE47qU7P16ah01" +
        "LqrZo4H+VbPrASrWrvdLPKCfXZumOeK3ypxwTH9B+r3tP1/DFpRnMf7rHtts5EBIErOzgfAAABUAGEMQB3Dp6Auwp2OGctRIcbfwSoUvZODaL+" +
        "T/ynyv786iujtNRItmmUkP9v3YgkUE3tl+mAAAAAAQwB/8peGGPeGsP/9DMpUI657xXtEUrjtriVNHchB12/jZ6s3wDV8e5WBspJtqYxRXZgAD" +
        "AdJpCcH7+BR8MABHpr8uAJjYKRZmAAAAFQAYQxAKIXvALkQ3mxpI8syQGyNK4SD48J0euFsgbTfDnVxj/eSHgAQ1Tf3LYq0iglXaotGIAAAAAB" +
        "DAH/6vhhhN4aw//0MylQjrnvFe0RSuO2uJU0dyEHXb+NnqzfANXx7lYGykm2pjFFdmAAMB0mkJwfv4FHwwAEemvy4AmNgpFmYAAAAVABhDEAoh" +
        "e8AuRDebGkjyzJAbI0rhIPjwnR64WyBtN8OdXGP95IeABDVN/ctirSKCVdqi0YgAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6RGAkAAAABQgEB" +
        "AWAAAAMAkAAAAwAAAwAeoEIIWWRGq8rwFoCAAAADAIAAAAyEAAAAAUQBwXGhEgAAASYBrx2Av4njYUvXEbtF2qc95nsqS5OPFApJc7/o89ZMYj" +
        "qGGgDBF5TkW5ktFZVKKkm4viCF6wbk63pepOtI1BqwZgHFtbwKQTHBtZLXFNPrWB///YWSDdvUogGnhjpMV9hLMkkfnYOpuFXNvRKdi+5SyX+I" +
        "p5okeD+SMvTugfd2zOkqFkalf/ajoq4ne8quNvHNifOeG5dpIVkQ6a8G+H2rLv6W3pADKmfuw84xssc7JY2y3jMpjArO9R+qdj5L3j22hkjQDW" +
        "OTth4WYrRfRHLgZ6X8WPh5xpWYEASYjUYb8lq1tYxvAMkS1gpoBNHjT3mdnGScqdbraDZYO5TTkDo4/jrrieRqyurcvD8hB72N10n3+mA8Sjs/" +
        "dPOcOD3MBI7OYjftRKCl1E2IncqU/QOW6ppd8IYNLKDpmRMBYAAAAVABhDEAvjfqgmkdQc1hwGKjt+jn2H154qWHQ9Tcmz+lqIZZKr23xsI4vT" +
        "O1HwMY1snq4w1ygAAAAAEMAf/KXhhjIbeB4GabcpTgenqUiCXAcQowHskMI+RQtjK3gDxVQD0I2q87TCNG7IiYFczI64WdavCAjX+MGepN7iuz" +
        "8Wir6MLjYnFV043hi1Fu495qURk81QZLbEAAAAFQAYQxAC/pTD45wggpxyaqBh1JuYe2Z3hT3pg0dP7B3VD0k2WsNln/Mlp5IGpzAx7Qa//IWI" +
        "AAAAABDAH/6vhhhIaqAAABUAGEMQC+N+qCaR1BzWHAYqO36OfYfXnipYdD1NybP6WohlkqvbfGwji9M7UfAxjWyerjDXKAAAAAAQIB0Al+EMYU" +
        "QPuQQv5tlpkBVP50sAAAAVABhDEAvjfqgmkdQc1hwGKjt+jn2KOKEa8dJ7Vo/ZQQo8tWjGtO0BUT2NOV0jHRDnXXBytmgA==");

    /// <summary>x265 4.2, 32x32, 4:2:0, 8 bits, 8 pictures: an IDR picture, two CRA pictures that are each followed by two RASL pictures, and a trailing picture: keyint=3:min-keyint=3:bframes=4:b-adapt=0:ref=2:qp=36:open-gop=1.</summary>
    public static byte[] X265OpenGop420 => Convert.FromBase64String(
        "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");
}
