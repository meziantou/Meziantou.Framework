namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The edge-preserving filters (bilateral, guided) and the radial blur. The references come from OpenCV 5.0.0
/// (<c>cv2.bilateralFilter</c> on gray samples, with <c>BORDER_REPLICATE</c> and <c>BORDER_REFLECT</c>, and
/// <c>cv2.ximgproc.guidedFilter</c> of opencv-contrib-python-headless 5.0.0.93, whose box filters reflect like
/// <see cref="ConvolutionEdgeMode.Mirror"/>), or from numpy 2.5.4 implementations of the documented definitions in double
/// precision, written from the papers (the bilateral filter of Tomasi and Manduchi with the root mean square color
/// difference, the guided filter of He et al. by brute-force window means for the other edge modes, and the radial blur
/// with bilinear samples), run in a <c>python:3.13-slim</c> container on <see cref="CreateSamples"/>, and kept as literals
/// (base64 of the samples, little-endian for 16 bits).
/// </summary>
public sealed class SmoothingTests
{
    private const int Width = 24;
    private const int Height = 17;

    private static readonly Dictionary<string, string> References = new(StringComparer.Ordinal)
    {
        ["bilateral_gray_3_clamp"] = "CQoPFhgiIys5PwsHk5qdqK6wvIaVkZueCAsUGhsgJSszDAoTn5+orqqwkZOQlKCgCxIaHyEkMycQDg4VoK2ptLCOi5SSnKCnCxQeIywyLhETDxQWqa+1t7SMkpCdpKSrEBkmLjMxOA8TGBgZsK+suYmUlpmbpqyvFyIpMTEsDhIZGx4esrS2jJOZl6OisbW7GiUxLzMQEhEWHyMjq6qLk5GZnKapta+7ISowMjUPFBQeIisvqKeQjJWVn6iusLyNLC0yLhIRFhciJzAzpYaJjpKboa2pu5KINDQxEhYUGyAhLC02f4+Ul5qnqrW2kpCQNjANDxcZGSIoMTE5i5OYnKWlqbi7i5GYPDgNDhYaIikpKTkWk5KXnaSstLaOlJicPQoNFhgcJygnMwwJkpicoaazspGMkpuiCw0QFx0fJC8yEgsIlpukpLCrs4uNlp+mERMXFyIkLC4sDQsNmaWjqa64ho2VmKOnFRYaISUuMyoLDAsNnqmtq7aFkpSSn6GpGBgdIS4tNREKEA4XqrCyq4aVkJWVn6Wq",
        ["bilateral_gray_2_mirror"] = "BQgPFhkdJi82MhAOkZmfqK6wsI+OkpqfBwsUGBshKCkrEw4Tm5+nra2ol5CQlJ2iChEaHyMoLSQUDw4XnqmutayWjo6Tm6GmDBQfKCwyLRgTDhMYqK22ta2Qj5KZoKeqEBonLjUxMBQTFxkbrrOvs5KUkpmep6yxFiErMzEpFREWHCAfsrCvkpaTm6Cmr7a5HCQwMy8YEA8XICUnrKiUko+YnaartbS3ISoyMiwTEBQbJSw0op6QipCWn6ixsLOZKzAwKhkTExkgKDA8mIuLjZGaoq6usZaNMzIqFxcTGB4kKzQ/eIyQlpyiq7SzmZGONywVEREXHiMoMDNAgo+XnKKpsraxkJKVOCwQDxYaISctLDEfipKZnaWtsbCVkpWdMRIOERgdJCwsKhIOj5eaoaixrZiPkJyjDg4OFhogKC4sGA0IlJuipbGtq4+MlZ+nEhAVGiIlLi0lEgsOlaKnrq2yj46PmKKoFRcaIScvMCgSDQwQnaiuqq+Pk4+UnKOoFxkdIywyMhoNCxAZpa+wqpKQj5OXnaWq",
        ["bilateral_rgb_2_zero"] = "AxINByEXCyQdEiQfEggeIAorLAoyJREKIRUJJBwLDiETDicZopmppYepp5GsqZaktZyPtZ2Htp6LjrGbkLSblqakmZOpopCvBiIXCS4iDywlGA4oIA0yKgwyMg42MRoPKB8OFi4VFTMbFDQgoYSrpYiup5CprJelsqCOtaCJja+Zj7SckrKgl5WonpOsp5OxCiwdEDAjGxEmHw0tIwovLhEzNB8SMx8PEiwTEjQZETQgFTgkn4mzo4+sqJSqrqGXsKORjK+UjbOajbSfkayhm4yroJCrrZeqFC8eGDEjIxAnJAoqJw8tLx0VLyURFisSDjAWDDIcEjEiIRMropO2pZasraCZrKOVr6mRi7GWjLWdjrKhm4yrnIyxppGtsZ2dGiQfJREmKAooKQ0lLRkUKyERKCkMEi8WDS4bEC4eGhgpJA4tppa3qZyoqqGQqqiRkq6Ui7SXjrCemZWrmZKxn461pJSys6CXHgwiKAsrKwwpLRAjKyEQJSgKFzAQES8ZESsfGBooHxMtJgoxqpuxqZ6Xp6WOkqyUjrKZjK+cl62jmZavn5G0n5S1rpuasqKVHwwjKQ4pLhAmMh8TKycOEzEPDjQUDjAeEyYjHhMuIhEwJw4ytZyVqKOUlquUj7Kai6+elLGknJOro4+wo5Gyr5eXr56TtKGTHg4iKhMmNBsUMSEPMikRDDUXDDYcEikhIBEsJBAvJhMuKRsbtaKVqaaWkrCai7Kfjqqjm4uqo42vp42trpCsrpyRrZ+SnqqXJhMnLhUsMBwUNCIRETMWDTgfDTMhHxUqJQ4uKBEwLRcoLh0VsKSXkK2ajbKci66flZKnnYqtoYysq5CpspiQr5yNl6iRjbCaLRYuLRwVLR8UGCwRFTUaDzYhEjQpHRcsJBMvKhQpMhsSMhwSjqubj66djbCckK2fnI2soYmtpI6ssZSPtZiKlamLjLCUia+WJhkKMCAPFC0QEjQWETIaEzQnGRUtHBYxIxMsLxoSMhwNMx8MkK6fkK+hk6uimpqpoo+ypo2xsZKMr5WMtJuFkK+Sj7GXjbSaMSMLMicLFTASDjUYFDEfHQ8uHRM2IBE1KRY0LB0PMCENGyUUk7Ohlaylmpiun5SypZC3q5GrrZSOrpmJl6aQkrGZla2fnJCiKigOGisSDzMXEi4bHw8qIgsxIwszJhIyLBcTKCAUFSQTDSwZlbCemaylnpewopS0qJW0rZmOq5yQj6SRjLCZj7GemKynn42qEiYREjEXETIaFCwgIwwvKAovKw4sMRUVLxoVGCMVDy0aCSoZl6idm5SooZKypZWsraGKrKOOpKeQjLCZjLOdkrSlnY+so4qyCyMRETEZEzEfIhAwJgs2KQsxMxYaLxsWKCATES4cDi0cDTIdm4yloIuxpI2yqZSuqKWKp6iLla2Ujq6bka2impKpopG0po+2CiMRETAdHRcmIhAxJQs2KRA1LxkTKiESDy0YDiwaDTAdDxgbn4qtp4ywqo+0q5+Po6eIi62Qi62Xj6ygl6moopaypZG0p5G3DigXEygbFw8eHwonIwwzIg4XKxkODh4OCiYTCCoXDCoZDA4UqZGxrpGztZaZsZ6UjK2Qg62VhKyajqqkn5mypJK0po+1ppGs",
        ["bilateral_rgba_2_wrap"] = "By4kBg02JQwRJSMWFhonIR8PLiwqDTQ3LxAkQjQUF00oHg5YICYRYxkuGm0iOyt1mYahiKOKqJCokKyaq5ifprOckbKsn4u9o6SRypOvnNeUraHhm5uq7Z6TqvSkkK76DDYnBg4oJgwWHyYWGhMqISMPMywtDi83NRUjQi0bFk0lIg9YGy0UYxg0Hm0kPDF1koCeh6SKrJCnkaiarZqgpa6gkLCpoo27layWx5GyndKTqqLdmJmn6Z+TrfGnk635Ey4lBRkiJAwaFCgWIA8wISYOKywwFSg3MhwcQiYiE00ZKhNYFTMZYxQzI20oOjZ2lISlh6OOq5CplqSarZ+apaqkkrCXrJK7kLKZxo6voNGSpaPbm5Gr56GRq/Gtl6n5HScjBh8WJQwiESoWJAwnISoSJiwvHBs3KyMXQh0pEU0RMBZYDjAdYxYsJm0mLDV1mYupiKWVqpCsnp2araOXpaSrkrCSsJW8jbCdxpCrotKYlKrdnI6w6KaSqvKwnKH5JBkiBiUVJAwnDSMWJw4kISsUHSwuIBc3JSkPQhktE00MMBlYDi4eYxgjJ20nIjN1noytiKibpZCroJqaqqiSpp2tlLOPsZa9ja2ey5adp9eYk7DioI6y7aaWrPSyn5r7Jgot/yoKL/8tDyn/LhYf/ysmEP8dLQv/EzEQ/xEvGv8UJSL/Gxcr/yEQMf8oDTL/sZ2g/6igk/+ep5D+jq2U/o6zmv2QrKD7maen/JqXsfmgk7P5opWv+a2bnvyxoZf+Jg4s/yoPKv8vEiP/Mh4V/ygoEP8WMg//DjQU/xAtHv8WISX/HhUu/yMRL/8nES//s52V/6ijlP+Yq5T/jrKa/4ytn/+WqqX/nJar/qORsP6lkq7+rped/q6elv+xopP/JxIs/ysUJf80GRj/MCER/yorEf8QNBf/DjMc/xMmIf8eFSz/IxEu/ycULP8pGSH/saOW/6Wnlv+TsJn/jLCf/5CmpP+Zkan/o5Cv/6eOrP+vkaf/rJuV/6uhk/+hqZb/KxYs/zAXKP8uHBb/LiMS/xcxFf8QNx//DzAi/xwbKf8kES7/KBAt/y0XJf8uHBn/qKWY/5Wsmv+PsZz/jKuf/5SXp/+cjK7/ooyq/6yQpv+wmJT/qp2P/5mokf+Rrpj/MRor/y4cGf8qIBX/GiwS/xc1G/8QNCD/Ey4p/xwZLP8kEy3/KxUl/zEbFv8yHBP/kaqb/5Gunf+Orp3/kqeh/5qRq/+hi6v/po6n/6+Ukv+wmY3/mqeL/4+uk/+JrZX/MyIR/y0hEv8ZKw//FDIV/xExG/8ULyf/GBst/x0XMf8kEyn/LhoX/zAcD/8xIA3/ka6f/5Ctof+TqKP/mpuq/6KQr/+mjKz/r5KU/62Wjf+tnof/la2R/5Cvlv+Or5r/NCcO/ywoDP8XMBL/EDIY/xUsIf8bFS3/HRQ1/yERMv8pFyz/KxwT/ywhD/8fJRP/krKh/5Wqpf+Zna3/n5ay/6WQsv+skaf/rJWS/6maiv+apo//lK+Y/5Sqnv+Zl6D/KiwR/x4uE/8SMhb/Eisd/x0VKP8hDjH/JAww/ygSLv8rFxn/JiAV/xglE/8RKxj/la2e/5inp/+dma//opSx/6mWrf+smZX/qJ2R/5Skkf+Pr5j/kK6e/5mmp/+ekan/Fy4V/xQzF/8SLxv/Fycj/yIPLv8nCi//Kw8p/zAVGv8rGxj/GyIU/xEtGf8KKhn/l6Kf/5uXqP+hk7H/ppap/6yfkv+qo5D/oaeQ/4+vmP+MsJ3/k62l/52Vrf+jjbH/EDEY/xIwGv8VLCH/IBUu/yUONf8qDC//MhUe/y0cF/8lIRT/FC0b/w4sHP8NLx7/mo6l/6CNsP+kjq//qZan/6ekjv+jqIz/lq2U/4+um/+Sq6P/m5mq/6GSs/+ljbT/DzEZ/xMvHv8cHSb/IhIx/ycLNf8rES//LhoY/yciE/8ULBj/Dy4b/w4vHv8QIB7/oIut/6eLrv+rkK7/qp6V/6CmjP+QrI//jq2X/o6soP6Xpqj+oZix/qSRsv6mkLb/EjMe/xYsIP8fGSn/JQ0u/ycNNP8rEiL/LRwU/xkoEv8QLxf/CjEc/w4sHf8SHSD/qZCu/66RsP+yl5/+rZ6W/pOrjv2KrJT8hqub+4+po/qcoa76oZWw+6OQsfyjj6/9",
        ["bilateral_rgb48_2_mirror"] = "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",
        ["guided_gray_3_mirror"] = "EhAUGhgiHyg3QBUOhJSVo6epso6dlp6dEBAZHRocIikzDw4nm5imq6Kql5uWl6GfFBgcHR0fMCcUEREpj6qes6mRkZyWnp6lEhgdHiUrKhMZEB0imKawtK+PmZSjpaClFxwjKCspNBMZIyIgp6OitYeZnJybo6amHSMjKiglExgiJCkirbC1iZafmqaerK6zHCMrJisXGRQXJCsonaKGlpSfnqekr6exHyUpKzAUHBglIjY9naWQiZuVnqSpqbWYJictKBgXHRknJzY+o36DjJKbnqeks5qRLC0qGBwaICUeLStAc4qVmJumpq+umJeZKSYUFR4gGCEnODRCf5Kcn6afobCyjpafLi0VFRwdJCooKUEejoiSmJ6mrK2VmpyfLhIUHR0bJiUlOA8RhZSZmp+uqpeTl52hEhQVHiAcHy81GBMQipamna2krJKTmp+jGBkcGCEfJywuFBQfhqObpKq0jZObmqGhGhscICEoLigQEhIVi6KppbaJmpyWoJuiGhgeHiglLxcPHBUtm6+wo4iZl5yXn6Ci",
        ["guided_rgb_2_mirror"] = "DyMeECUhEiYkGiYhHRMmJRMpLBEuJh4UJh4WLCMVEikXHDImoJinnYegp5ajo5qiqp+VrJ+Qrp6Wk6adk66dnaOkm5amoJGtDycgECsjFSklGxUmIRQrKBUoKhIuLRwUJyIaGywZHi8hIjUml4Sbo4uoppSip5mjqaGVrqGRkqmdl6uamqqhnJapnpiopZawEikjFCkjHBYkIBYkIhIpKRUrKCAVLRwXFyYYFywXHy8mJTwxlYShnJGfppinpqCXqaGXk6mZl6edlaufl6efnJKnn5aop5mpGSoiGisiIBkjIxIoIhgoKBwaJyMXGiYYFyUYGDAiFy0jJR4soZOqqZunoZ6Yo6CZq6OXk6qbl6yck6yioJSln5SqpZerqpyeHyMjIxUkJRUkJhYjJh4XJh4aJiUVFikcFSkbHC0fGhwnJx0ropOpo5yjo5ySp6WbmKibkaubl6menZemnJumoZauo5atqp6bJhQnJhIpKRMmJxgjJSEYJCYTGioWGiUcGyghGRsiIx0pLBw1qqGtnJeVpaOTlKSYlKublaeemqiim5qon5qqoZmsppycrJ6cKBMmKBgkKRclKSEXJyQXGCsWFioZFywdGCciHhgoJx8sLhw0opOQnp+Vk6GVmKielKmfmauhm5ikopamoZarqJqZqp6Yr56ZJxgjKxcjKh8XJyEWKyQZFS4ZFS0fGSYgIBciJBUrJh8tKBwdpp+Xpaidlqeak62flaWin5KnoZWlpJalp5Wpp5+XqJ6anqSbLBojKRcpKx8YLB8aFiwbFiofFiwdIRgmJBUlKB8uMSIuLCAfpqWajZ2Vk6mek6agmZiin5SlnpmnpZSlqJuWqpyVnKWVlaqbLRopKCEYKiAbHSgWGisaFywiGS0jHxwlIhwqKRspKhkXMiUii52PkqGckKaZmKmhoJSln5Kln5mnp5iWrZiRmKSUlKmZlKiWKCMTLSEWGSkWGCwcGikcFywkHxgkHxsmIhwpIxgWLR8XMiUfjJuWmaqjmqihmJmgoZaqopWrp5eQppmYrZuRlKmVl6malLCZKycULScSGygYFS4cGisfIRYoHhorIBksKhkwKR4XMSgeIicglqihk6OfmZSmnZeloZisppelp5mVqJySm6CWl6ibmaWfm5SeJyoWHCgWFS4bGycdIRYmIBUoIxMuIhopJRgXKCQfFx4SHC4hkaGXnqimnpqpopqroZmspZqSpJyZlKGVk6aclamemqiknpakFy4YGSgcGSwaGScfIRQqJRYmJhUpJxkZKx0aGSAbGyofFCcblZ6blJCbnZenopqkoqGQpaGVoqSakambl6qemKykm5OmoJCsFioZFi4dGSweIhUoJREtJRQrKRkcJhwcJSAcFisfGSweIDcoj4mWm42npZWspZmpo6KTpKGYmKeYk6edlqWgnJSloZetopiwEysbFS8eHhskIRkpIhQsIhUuKhoYJh8bFSgcFyccGDAiERwbkoSZpZOpppWuoZySoqWRkamWkqeZl6ahmqekn5msoZawo5WyFS4gFywgHRolIhQpJRIvKBYfKxwYGCMbEykaFyscFTIiGCAio5OrrpexqJWXrJ6blKeZkKeakKmbk6einZmqoZivopGxoZSq",
        ["guided_rgb_10_mirror"] = "EhkTFiEdFCojJDQhFQ4bIxcoMxk5Hx4RJBkYMSAXECMZHCwpn6mkmIKbpJmjnJumpJuUpJWLtJudjZyaj62XoqqkkpCXno+oDyIZGC8lHjQpFxAgIxovLh0sMBo5LRsVKyQgGzAcIi8kJjUjmIWVoYmno5OipZmppJ6Trp6Ri6KblqiUo7OpmZGnmpifrp+1EyojGi8lHQ8dIRYhIBIpNSI6KCUVNR4dECQYFi4XIy8qJkEykYShl5SdpJmuoqCTq56bhZ+QlKCcl66hlKaZkoSampSgrqKqIDMiJDooIhggJRIsJB8yKBwZLyweFiYYFCEWHDcoGDAkKRYqn5etqp+qnJ6UoJqYs6WdiqKVma2clbKnnomdmo+lqpyvqJ2VIy8jIhIfJBQiKRgoIRwRJhscLywbFCsgEykZITQfGRgoKxkmoZSuoZ6noZaLraaglaWaiaSSl6mbmouglZWeo5exoZiwpKGUJBYlJhYtKxMpJxkpIiEXKSsWFioTGyMdIi4mGBcgJh0nLhs1qqS1mpOUp6CPi5uRjqaTkKCYo7Gol5amoJ2sop6znJiUq6SeIREiKCAoLBsuKiUYLSoeECcRDyQUHTQkGS0mIBYqKiQsLhs2po+QnpyVjJyQmaWhj6acpbepk4yao5Skn5SunpCOp5yWs6KgGxwiKxsoKCIVIRoROi4nES0YGTMmHCojIREeKBUtJCQwIiIUq5ybq6ehlaSYkq6fkqShnommoJCippenrpqzpaKWp5qdmKWfLCQsKRg1Kx4ZNiIjEi0dFikiGC8cJRMoKBMkLSc1NCYzJiUarqGeiJiUkqiekKWdjYmUnY2fnp6qpZSqo5mSq5qXk5+PkaqhNCM8JCMbLiEhFiMQGywZHDIpJj0tIBwkJSIvLB4uKBsULyYjiJ2Nkpqdi6OSnK+foYygmIqfn56toJSRrJaRi5uLjqSai5yRHxsRLhwaDiQSGS8hGygYHzguHBAeHRwkIiEsHRkQLCMWMyQgjJeZnKuknauekJGXo5Wso5awnY+FopWYsZ6UiqSPmaiembOdMCodNSwZFiIVEzIeIzYkIxInHx8uIx8xNx49KiIWNS0hGyUimqqikaSdloukmpCgpZ6yq5qsopKRqJySkpmRmKqdnqWklIOWMi4iFR0SEzAeHyoeIhEiHBEjJxYzKCQxIhURLSojERsPFTAik6WUoa6loJioppyuo52znI6IpJibg5OIi6GZk6qco6+ropChGS0cGiMhGy8ZGCwfHxMpKxsnLhswJBYRMyIeFR0cGyoiEiQal6WXkYmVmZemoZikmJiFqKSWqaqhiaWYma6gn7aqkomcoIyrEx8TGzEjIDYjJhIoKhQwKhwxLB8cKB8dKyMfFi8jHSweIjsmj4mTmImmpJOtqJqvoZ+QqJ6alaSWjqKalKObk4SXppmqq6GyDyITFzIfIRYiKCAtIxouJh42JxoTLywkEy0eGSMfHTIlFQ8akYOWpZWnpJexmpWKn6SOh6KPi5yRnKajo7CnopmpoJmtpJuxHzIqJTIiHBIfJRUnLBw2JhYZMCUbDyIXESoYHS0eGjYlHxQioZurr52yppGRr6KfjaaZh5qVjaOXk6Wkn5CpqpyxopCqnJaW",
        ["guided_rgb_2_wrap"] = "DiQbFCYiFyYmHyYjIBMmJRQoKxMsJB8VJB8XKSMXECkYGy8noZmnnYegp5ajoZuiqKCVqaGSq5+Zk6afkaqcnKKmkY6emoumECgfGC0oHSoqHRMnIhQsKBUnKRQsLB0VJSMbGC0aHS8iIjQml4SbpIupppWippqip6KVq6KSkKmdlqqbm6qkmZSomJOjqpiyGCwoHCspHRQkIhYlIxIpKRUqKCEVLB0XFScYFS0YHi8mJTsylYShnJGfppmnpqGWqKKXkKqZlaeelKuglaWflIyhmZGiqZmpJTIrJjEsIhklJRMpIhgoKBwaJyMWGicYFyYYGDAiFy0jJR4soZOrqZunoZ6Xo6CZq6OXkqqblqydk6yjnpKkm5Gnp5eso5WYKi0sIxYlJhUkKBckJh4XJh4aJiUVFikcFSkbHC0fGhwnJx0ropOpo5yjo5ySp6WbmKibkaubl6menZemmpmkoZWtopSropiUKBorKRctKxUnKBgjJSEYJCYTGioWGiUcGyghGRsiIx0pLBw1qqGtnJeVpaOTlKSYlKublaeemqiim5qon5qqopmtnpWWqZ6aKBUnLR4oLRonKiIYJyQXGCsWFioZFywdGCciHhgoJx8sLhw0opOQnp+Vk6GVmKielKmfmauhm5ikopamoZSropOUppqVraCZJxsmLxwmLCAYJx8VKyQZFS4ZFS0fGSYgIBciJBUrJh8tKBwdpp+Xpaidlqeak62flaWin5KnoZWlpJalqJaqpp6WpZqYmaCWNSctLh0sLCAYLiEbFiwbFiofFiwdIRgmJBUlKB8uMSIuLCAfpqWajZ2Vk6mek6agmZiin5SlnpmnpZSlp5mVqZuUl52OkqWYOyw2KiQbLCMdHCcVGisaFywiGS0jHxwlIhwqKRspKhkXMiUii52PkqGckKaZmKmhoJSln5Kln5mnp5iWrJiQlJ2OkaOViZmLKCAULyIZFykUGS4dGikcFywkHxgkHxsmIhwpIxgWLR8XMiUfjJuWmaqjmqihmJmgoZaqopWrp5eQppmYrZyRkaSQl6eZlKyZNDAgMS0aGigXFS8dGisfIRYoHhorIBksKhkwKR4XMSgeIicglqihk6OfmZSmnZeloZisppelp5mVqJySmZ6Ul6iamaWejIaNMzUmGiUUFjAcHCgeIRYmIBUoIxMuIhopJRgXKCQfFx4SHC4hkaGXnqimnpqpopqroZmspZqSpJyZlKGVkaWak6ibnKqnmI+cHjMeHCofGS4cGSggIBQqJRYmJhQpKBkZLB0aGiAbGyofFCcblZ+blI+bnZenopmko6CQpqGUo6Sakamblquema6mkY6bmoymFCkWGzIjHDAjIhYqJBEtJRMsKRkdJxscJx8bFyofGiwdIDcnj4qWm42npZSsppiqpaGTpqCXmaaYlKeclKWdlJCcn5erpZmyDyoYFzEiHR0mIhsrIRQsIxQvKhkZJx4bFyccGSYcGTAhEh4akoWZpZOpppSuopuSpKORk6iWlKeYmKefmqqjnZqonZasoZOwIDctHTEpGxkmIRUqJBIvKBYgKxsZGiEaFicZGisbFjMhGiMho5SrrZawqJSYrJ2bl6Wakqaakqmak6ihnJmmopqunpCrk4yd",
        ["guided_rgba_2_clamp"] = "EyElCxgcJxAdGCkYIhYnISUUJSwoFiE3KBkeQiUeGE0jIhlXJiodYB4uIWgrOzRxlIubjJqJnZGmlqGdpZufpqmelbCqn5K5qZ+WxpWmnNCTrJ3anaGl55yXp+uikK/0HRYoER8WKBciFSceJBUlJyUWIzMnGR8+JhsdSCQfGFMiJBtdICseZiQxKW4uOjV0iX6QjZyKn5aklZ+eppyeqKehlbKpopK8laibx5epm9GZp6LgnJep55+YqO2plbH3IxApHSQSJyIlFCUqJRciNCUZID4kHR1LIiEZVCEiGV4bJRpmGygebiMuK3cwOjx8hHeMlZWJl5qhlp6lpJ6YrKailraXp5e8l6abypWpn9aXpqDdm5Sn5qGXqO6rman3JQ8nLyYSJDYmFSI9JRgfRyMdHFEiIRlbHyQYaBslGm4YIxt1GycjgR0nKIMnIzKIhXiKo5eLlKiclZOpoZ2XsKSjl72VqZnBlqmc0JSootuelqThn5Wp6qeXqvStnZ33Jg8lSyYSI1EmFiBZJRodYyIgGGogIhh0HSUYghkmG4gWIxyNGiMhlhoaJpklHC2bfXCAtI2Fi7OYj4qxnp+VvpimmcGTqZnHlqad1ZqYpN2bmqXloZas8KSXrPStn5v5KQ8puyoPKsQrEybBKRkhviYjF7giKRTHGCwWvhknHb8dKSXKHRwovSohMcQ2JD/Grqaq9KShmuinqZftl6mb55StnO2WqKDtmqel+JybqvKhmqz3o5qt+aidnfiuoJv8KREo0ioVJd0rFiTiKiEW2ycmF+IXLRbSFSwZ0xYsHuQaKCXeIRkr2ywiMd0zIDncppiS9KKkmPOXppjwmKqf+JSqoPaYqqP/nJml86KXp/milqz6qJuZ+aufmPywoJj+JxYk5CsXI+8rHxfpJyEV5CokGfkULxnqFS4f8hkmIe4gFyTpJRYs7ykgL+0sHh/np6GX/KaqnfyXqZv6k66g/JWlo/ufk6j6oZWm+6SWpvyolqn+qJ+X/aiemv2dppv9LBkk+yoXKvsrHxj3LSAa+xYtG/cWKx/4Fiwe+SEYJ/gkFSb4KSAv+zEiL/ouIR/3pqWa/o6elvyUqp7+k6eg/ZmYovuflKX9npmn/qWUpf6om5b+qpyV/pyllf6Uq5v+Lhsq/yghGf8qIBv/HSgW/xorGv8XLCL/GS0j/x8cJf8iHCr/KRsp/yoZF/8yJSL/i52P/5KhnP+Qppn/mKmh/6CUpf+fkqX/n5mn/6eYlv+tmJH/mKSU/5Spmf+TqJf/KCMT/y0hFv8ZKRb/GCwc/xopHP8XLCT/Hxgk/x8bJv8iHCn/IxgW/y0fF/8yJR//jJuW/5mqo/+aqKH/mJmg/6GWqv+ilav/p5eQ/6aZmP+tm5H/lKiV/5epmv+UsJr/LCcU/y0nEv8bKBj/FS4c/xorH/8hFij/Hhor/yAZLP8qGTD/KR4X/zEoHv8iJyD/lqih/5Ojn/+ZlKb/nZel/6GYrP+ml6X/p5mV/6ickv+boJb/l6ib/5mkn/+bkp//KCoW/xwoFv8VLhv/Gycd/yEWJv8gFSj/IxMu/yIaKf8lGBf/KCQf/xceEv8cLiH/kaGX/56opv+emqn/opqr/6GZrP+lmpL/pJyZ/5Shlf+Tppz/lamd/5qno/+flKX/GC4Y/xkoHP8ZLBr/GScf/yEUKv8lFib/JhUp/ycZGf8rHRr/GSAb/xsqH/8UJxv/lZ6b/5SQm/+dl6f/opqk/6KhkP+loZX/oqSa/5Gpm/+Xqp7/mKuk/5yTpv+hj6z/FioY/xctHf8ZLB7/IhUo/yURLf8lFCr/KRkc/yYcHP8lIBv/Fisf/xksHf8gNyf/kIqW/5yOp/+llaz/pZqp/6OilP+koZn/l6eZ/5Onnf+WpaD/nJOl/6GWrP+jl7D/Eysb/xUuHv8eGyT/Ihkp/yMULP8jFC3/KhoY/yUfG/8UKRz/Fycc/xgwIv8RHBv/koWZ/6aUqf+mlq3/opyS/6Gkkv+QqZf/kaea/5elof+ZpqX/n5mr/6GWr/+klLH/FS8i/xgsIf8eGSX/IxQp/yUSL/8pFh7/KxwX/xckGv8SKRn/Fiwc/xUzIv8ZICL/pJSr/6+Xsf+qlZf/rJ6b/5Onm/+Pp5v/j6ic/5Kmo/+dmKv/opev/6ORr/+jlKf/",
        ["guided_rgb48_3_zero"] = "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",
        ["zoom_rgb_clamp"] = "DR8XDh0bDhwjFyIqIR8lJQ0qMQwrKxccIx8LLiMMESsXGDctn6KnonuqqZOspZmpspyLv5qHpaGTibGZkrObmKWkmZKsnIaxDR4ZERwdExskFxkrJB8qKRchLRAqKxYdHyUPHiwSFy0dHTkvnZSso4OtqJWtqZuktJ+HvZ+LiKqYjLaYlbOjlJ2nm4ysooq0DSgiER8kFRonHBooIxUmKxoqKB8bKxgdGykTEzQYFS4iJjQxnYaypo+wqZipraCUt6KMl6uUi7GYkbmhkaikmpKppYyxrZS2FysjGSclHCAhIhMiJxIjKxocKCcgGisOGioSCzIdESclLCU1nIm/pZuvqp6Ys6KOp6uTjK+ZirqajbCgmYymoYysqpassZmpIiUjJB4jJRweKRwZKBkUJR4YJSUUHDEYDCoYFSwbFBotMxI2ppXEqJ2pr56FtKmQkK+UiLeUja+fmpanm5OxopOyqJmorJ6cKBMqKRQoKxQkKBUeIx4UISkKHC0PGDAYEyYlFhkjIhMrNA9AsJu5p5mWrqWEj6qRhrOVjbacl6mol5uwnZW3opivqZ2gsKGVKAwlKxEiMBggLCEaIygTFy8QDzISDTAbEiYpGRMwJRYtORk7vJWWrKKQkqmPjrGdjLiilK+mnJiroI6upo2or5Sct5qZvZ+TIRYfJxgaLh0WLCMSJywSHTMZDjIeFSMiIBAmJAkzIRctNSoawJ6UsKyZlLOaiLuhjaiqmY2spoevrI2wsZWosJyhqqKeo6ibMhkrMxknKx4kJSIfGy0aDy8eEyMjIBQjKwgvLRc4MhwvOi8cxKaXh6WbhbOghqqhjo+gl4upoo2uqpOkrZeYpp+Tm6qUkbCaMxowLh4kJiQZHSsTFTQYFDggGDErHSIuIhYzLBMsMxcPPiweiq2PjaehibOYl7GeoJCon4irpI6iqpWWpJyNmaKLkaaMh6mOKR8IJCQKHikNFTIWEDUdFS0kGBoqGRIuHhYsKRYPOB4DPSsgiaifk7GlmK6flZqjo5O0qoqlrI6WrJiOqaKNoKePm62RkbSVNC0NKzAPITIREjQZFS4jHBQqGg40IRFALhMxNRwKNiYMHS0lk7SikbOmmZOxnpOvpJKtr5KqqJSYpZyMn6SQoaOYn5iZmpGcJDETFjMVEzMZFyYeGhcmGgsyJA04Lg83MhUQMyMRFCIRDjcmlbiXmKyknJayo5Osp5inrZugnp2alKOTk6aXl6Gemp2moJapDi4UECwWFCcaFiMjHg8vKgg3LRAzMhINNBwQGCMXCywYET4jlaeVm5apopa1qZiqrJ6YpqWboKaUlKWXk6KclKWmmZ+un5mzCy0SDTEYEygiIBUrKgo0Kg41LxMeOBkMJCQXCiwdDTAcFS8kl5WcoIutpI+xp5qhn6WPn6WLn6yUnK6elaSjlp2on5+wo522DzYgFS8mHx0sJREuJgw0KxAwMxQPNCQRCi0ZDC4aDjcdGyInm5Coqo2yro+ppp2cmqeTj6yLlKiZnaOknaSqm5ynnpajopOoFTMpHSkpHx0sIREvJwwzMBAiNRwQHigSBC8XDjIYDDkfHhQpoJGxs5G2t5CbraGcjqqViqyQiqybk5+noJuwo5uso5ekopKe",
        ["spin_rgba_mirror"] = "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",
        ["spin_gray_360_zero"] = "ChQnMjc+SFZgZGVlZWVkYFZIPzczJhQKDyExNz9LXGRlYV9eXl9iZWRcSz43MSEQFyw1PEhcZWNeW1paWlpbXmNlXEg8NCwXITE4QlZkY11aWlpZWVlbWV1iZFZDODEhKTQ8S2BlXlpaWl5fX15ZWlpeZWBLPDQpLjY/VGRhW1paYF9fX2BgWltbYmRTPzYuMDdCWWVfWlleYGBjYl9fXlpaX2VZQjcwMThEXGVeWllfX2JzcGNfX1laXmVcRDgxMThEXGVeWllfX2Jwc2NfX1laXmVcRDgxMDdCWWVfWlleX2BjYWBgXVpaX2VZQjcwLjZAVGRhW1tZYGBfX19gWlpbYWRTPzYuKTQ8S2BlXllaWl5fX11aWlpeZWBLPDQpITE4Q1ZkYl1aW1pZWVlaWl1jZFZCODEhFyw0PEhcZWNeW1paWlpbXmNlXEg8NSwXECExNz5LXGRlYV9eXl9hZWRcSz83MSEPChQmMzc/SFZgZGVlZWVkYFZIPjcyJxQKBAsXKTI3PEJLU1lcXFlTS0I8NzMpFwwE",
        ["zoom_rgb_cap4_wrap"] = "W19uYVhkO0NJOkM7UVhaVV5oW1leVlhkbmiBh2+Rjm6PZFxeX2FcZFtjYWJjaWFfXGBeV19SZGVqZV5uYlxgXmFnX2RiYmFdWWRmWWJfNUNGN0BJXlNjaE9dY1dcUVZkbWyCiWuVinGKaV9lbVdZZ1tjYGVVWWxYW2VXV1xdZGRsY2RiVmtjWGJlYGRobGNoXmBrY1RfQEJKP0JGY05ZYUxjX1djZFpjb2x+hHOQiniKZWBdYGdfVl5iYGhiZWRVW2FXXmJcYWNuYmZrYmJeX2JkZWxhamNfZV5nYlZZO0hIQEc/W1ZhXVVhYVtmVFtrcHCFh3GIjX2BY2dgXF9fWmFjYG1hX19bWmBdYlVXaG1yZ21pbGJfW2hgZGdbZ2FcZVViXllZQEg8QTZAXFJkX1hdX11kWVZld2OAhHWPjn16YmVlV19kW2ZbY2FaWWFhXFhgY1FVbGlzbGJhYWRfY2dQaF5dYmVjXlhgXVtaQkBEPjlCX1BaXFtkW1VsZVtsfWd+iHd+iIJ4WFxlXGJpY2dfV2BeXmJjYVJdZ1Jcc2JsY2VnYGVeZ2NVYWFaW2FSYVteY1RXTThJPDtHX1phW1hoXVNsZFNqf2t5jIF5coh8WWVqYF9lXl9kXFxlaVNkaVZeY1dZcWdqZmpjYF1VaWJbWGFbW2RZZV9jaFRdPT1KPz0+V1ZjY1dpZVpqalhqf3JwdIR4boyAWGJiWV1gWWNkYVpqaVleZVhVX1lVb2ZmamZhaltdWWJcXWJdUWVdZFRjVFpiQkFHPkNFW19jYVpqZlVocVledndvb4qBc4mDY2deW1pfYFpkaU9haFpiX2BSVV1LdGtrbl9kZGFkX2JYVWphVmpnXVtgUmBfREVHOj1PWldmZVZsa1labWRcZ355c5GId32GWV9hYVxjZ1BacFZhbl1WXGFYVl5OdWddaV9jWWRWXmpdUWhoVltsU2BoVWNcQEJQPTlRXExsbFNrbGBXU2hcZ4B5eYqJfX+MWl5hZVNga1NiZVpfaWZcYGNaXFBTdWNjY19mWWVXU2JfWl1qXk9yVV9hUl5bRzdRQDtKY1dicFZeW19dWGJXaH1zeoSFfn6IXl9bYV5YaV9iY11gZmdVaVtRXU9bZGxmYWFjWGRdXFlgXF9maFVrXmVpYldfRjtJRztFYk9bYFpiWWdgXGhVbHtue3qKhXKIXV1dY2FbYFtgZl1ccVxPZ1tTZltcYl1mYWVrZV5hXlpmXV1nZE9nYllhXFRfRD9KRjxDY1ZgV2RbXGZaUmlYbnR0hm+Ii3SDZGVZYF1dY1tlbmJUa2VYZWBdXlBcZWluYWZhZ1lkW1lqXlRqZlNpYl9fW1RdSDxKTDRHYlhkX19VWmZUVWVZc252hm2KinZ9Z1hla1RgcllaZ2dTZmdYX1laWk1fZmtrY2hdVmJhXFVrYk9tZlZyWWhlWVNkSzlRRzZPZ1RaYGZbWG1bYlxee2NyiWuCjXODaVVldFtTbGFQXm1UXmpeWFReWFhfZW1gWW9kVWFoYE9wbFFzblVnWVdpX0lpSDhUSztJZl1YVGlbVmtbXltkeGZ5i3V5hXuDc1pcbVxQZGVVVm5fWlxhWFhjW1pVXXNmV2dkWktkXU9xblNpbFxS",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("bilateral_gray_3_clamp")]
    [InlineData("bilateral_gray_2_mirror")]
    [InlineData("bilateral_rgb_2_zero")]
    [InlineData("bilateral_rgba_2_wrap")]
    [InlineData("bilateral_rgb48_2_mirror")]
    [InlineData("guided_gray_3_mirror")]
    [InlineData("guided_rgb_2_mirror")]
    [InlineData("guided_rgb_10_mirror")]
    [InlineData("guided_rgb_2_wrap")]
    [InlineData("guided_rgba_2_clamp")]
    [InlineData("guided_rgb48_3_zero")]
    [InlineData("zoom_rgb_clamp")]
    [InlineData("spin_rgba_mirror")]
    [InlineData("spin_gray_360_zero")]
    [InlineData("zoom_rgb_cap4_wrap")]
    public void Filters_MatchTheReferences(string name)
    {
        using var image = name switch
        {
            _ when name.Contains("rgb48", StringComparison.Ordinal) => CreateImage<Rgb48>(3),
            _ when name.Contains("rgba", StringComparison.Ordinal) => CreateImage<Rgba32>(4),
            _ when name.Contains("rgb", StringComparison.Ordinal) => CreateImage<Rgb24>(3),
            _ => (Image)CreateImage<Gray8>(1),
        };

        switch (name)
        {
            case "bilateral_gray_3_clamp":
                image.BilateralFilter(3, 2.0, 0.1, ConvolutionEdgeMode.Clamp, Ct);
                break;
            case "bilateral_gray_2_mirror":
                image.BilateralFilter(2, 1.5, 0.2, ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "bilateral_rgb_2_zero":
                image.BilateralFilter(2, 1.5, 0.1, ConvolutionEdgeMode.Zero, Ct);
                break;
            case "bilateral_rgba_2_wrap":
                image.BilateralFilter(2, 1.5, 0.15, ConvolutionEdgeMode.Wrap, Ct);
                break;
            case "bilateral_rgb48_2_mirror":
                image.BilateralFilter(2, 1.5, 0.1, ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "guided_gray_3_mirror":
                image.GuidedFilter(3, 0.01, ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "guided_rgb_2_mirror":
                image.GuidedFilter(2, 0.02, ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "guided_rgb_10_mirror":
                image.GuidedFilter(10, 0.01, ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "guided_rgb_2_wrap":
                image.GuidedFilter(2, 0.02, ConvolutionEdgeMode.Wrap, Ct);
                break;
            case "guided_rgba_2_clamp":
                image.GuidedFilter(2, 0.02, ConvolutionEdgeMode.Clamp, Ct);
                break;
            case "guided_rgb48_3_zero":
                image.GuidedFilter(3, 0.01, ConvolutionEdgeMode.Zero, Ct);
                break;
            case "zoom_rgb_clamp":
                image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Zoom, new PointD(12.5, 8.5), 0.4), Ct);
                break;
            case "spin_rgba_mirror":
                image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Spin, new PointD(3, 20), 30) { EdgeMode = ConvolutionEdgeMode.Mirror }, Ct);
                break;
            case "spin_gray_360_zero":
                image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Spin, new PointD(12, 8), 360) { EdgeMode = ConvolutionEdgeMode.Zero }, Ct);
                break;
            case "zoom_rgb_cap4_wrap":
                image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Zoom, new PointD(-6, 4), 1.5) { EdgeMode = ConvolutionEdgeMode.Wrap, MaxSampleCount = 4 }, Ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, message: null);
        }

        // The double-precision references round a few samples on the other side of a tie, at most one unit away
        var expected = Convert.FromBase64String(References[name]);
        var actual = new byte[expected.Length];
        image.Frames[0].CopyPixelBytesTo(actual);
        var sixteenBit = name.Contains("rgb48", StringComparison.Ordinal);
        var step = sixteenBit ? 2 : 1;
        for (var i = 0; i < expected.Length; i += step)
        {
            var e = sixteenBit ? BitConverter.ToUInt16(expected, i) : expected[i];
            var a = sixteenBit ? BitConverter.ToUInt16(actual, i) : actual[i];
            Assert.True(Math.Abs(e - a) <= 1, $"{name}: sample {i / step} is {a}, expected {e}");
        }
    }

    #region BilateralFilter_SmoothNoiseKeepEdges
    [Fact]
    public void BilateralFilter_SmoothNoiseKeepEdges()
    {
        // A dark and a bright half, both with a little noise
        using var image = new Image<Gray8>(32, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var noise = ((x * 7) + (y * 13)) % 5 * 2;
                image.Frames[0][x, y] = new Gray8((byte)((x < 16 ? 40 : 200) + noise));
            }
        }

        // Differences of about 10 levels of 255 are smoothed, the step of 160 levels is kept
        image.BilateralFilter(radius: 3, spatialSigma: 2, rangeSigma: 0.1);

        Assert.InRange(image.Frames[0][5, 8].Value, 42, 46);
        Assert.InRange(image.Frames[0][15, 8].Value, 42, 46);
        Assert.InRange(image.Frames[0][16, 8].Value, 202, 206);
    }
    #endregion

    #region GuidedFilter_SmoothNoiseKeepEdges
    [Fact]
    public void GuidedFilter_SmoothNoiseKeepEdges()
    {
        // A dark and a bright half, both with a little noise
        using var image = new Image<Gray8>(32, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var noise = ((x * 7) + (y * 13)) % 5 * 2;
                image.Frames[0][x, y] = new Gray8((byte)((x < 16 ? 40 : 200) + noise));
            }
        }

        // Variations below √0.01 = 0.1 (25 levels of 255) are smoothed, the step of 160 levels is kept
        image.GuidedFilter(radius: 4, epsilon: 0.01);

        Assert.InRange(image.Frames[0][5, 8].Value, 42, 46);
        Assert.InRange(image.Frames[0][11, 8].Value, 40, 50);
        Assert.InRange(image.Frames[0][20, 8].Value, 200, 208);
    }
    #endregion

    #region RadialBlur_Zoom
    [Fact]
    public void RadialBlur_Zoom()
    {
        // Vertical stripes, 2 pixels wide
        using var image = new Image<Rgb24>(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                image.Frames[0][x, y] = x / 2 % 2 == 0 ? new Rgb24(0, 0, 0) : new Rgb24(255, 255, 255);
            }
        }

        // A zoom blur toward the center: the center stays sharp, the stripes far away along the horizontal axis blur to gray
        image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Zoom, center: new PointD(34.5, 32.5), amount: 0.5));

        Assert.Equal(255, image.Frames[0][34, 32].R);
        Assert.InRange(image.Frames[0][10, 32].R, 96, 160);
    }
    #endregion

    [Fact]
    public void RadialBlur_SpinOfRingsChangesLittle()
    {
        // Concentric rings are invariant by rotation around their center
        using var image = new Image<Gray8>(41, 41);
        for (var y = 0; y < 41; y++)
        {
            for (var x = 0; x < 41; x++)
            {
                var distance = Math.Sqrt(((x + 0.5 - 20.5) * (x + 0.5 - 20.5)) + ((y + 0.5 - 20.5) * (y + 0.5 - 20.5)));
                image.Frames[0][x, y] = new Gray8((byte)Math.Round(127.5 + (127.5 * Math.Cos(distance / 3)), MidpointRounding.AwayFromZero));
            }
        }

        using var original = image.Clone();
        image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Spin, new PointD(20.5, 20.5), 40), Ct);

        // The center pixel keeps its value; the others only lose what the bilinear samples average across the rings
        Assert.Equal(original.Frames[0][20, 20], image.Frames[0][20, 20]);
        for (var y = 10; y < 31; y++)
        {
            for (var x = 10; x < 31; x++)
            {
                Assert.InRange(Math.Abs(image.Frames[0][x, y].Value - original.Frames[0][x, y].Value), 0, 24);
            }
        }
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("guided")]
    [InlineData("zoom")]
    [InlineData("spin")]
    public void Filters_KeepUniformImages(string filter)
    {
        using var image = new Image<Rgba64>(20, 12, new Rgba64(1000, 30000, 65535, 65535));
        Apply(image, filter);
        Assert.All(ReadPixels(image), pixel => Assert.Equal(new Rgba64(1000, 30000, 65535, 65535), pixel));
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("guided")]
    [InlineData("zoom")]
    [InlineData("spin")]
    public void Filters_DoNotSpreadTheColorsOfTransparentPixels(string filter)
    {
        // The right half is fully transparent and hides a bright green
        using var image = new Image<Rgba32>(20, 12);
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                image.Frames[0][x, y] = x < 10 ? new Rgba32(200, 0, 0, 255) : new Rgba32(0, 255, 0, 0);
            }
        }

        Apply(image, filter);

        // The colors are filtered premultiplied: green never appears, and red keeps its value where it is visible
        Assert.All(ReadPixels(image), pixel =>
        {
            Assert.Equal(0, pixel.G);
            if (pixel.A > 0)
            {
                Assert.InRange(pixel.R, 199, 201);
            }
        });
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("guided")]
    [InlineData("zoom")]
    [InlineData("spin")]
    public void Filters_ProcessGrayFormatsLikeGray8(string filter)
    {
        // La16 is processed as RGBA with three equal colors: it gives the gray result. So does Argb32, a color format, except
        // for the guided filter, whose guide is then the three colors
        using var gray = CreateImage<Gray8>(1);
        using var grayAlpha = gray.CloneAs<La16>();
        using var argb = gray.CloneAs<Argb32>();
        Apply(gray, filter);
        Apply(grayAlpha, filter);
        Apply(argb, filter);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                Assert.Equal(gray.Frames[0][x, y].Value, grayAlpha.Frames[0][x, y].L);
                if (filter != "guided")
                {
                    Assert.Equal(gray.Frames[0][x, y].Value, argb.Frames[0][x, y].R);
                }
                Assert.Equal(255, grayAlpha.Frames[0][x, y].A);
            }
        }
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("guided")]
    [InlineData("zoom")]
    [InlineData("spin")]
    public void Filters_KeepTheSixteenBitPrecisionAndTheFloatingPointSamples(string filter)
    {
        using var wide = CreateImage<Rgb48>(3);
        using var floats = wide.CloneAs<RgbaFloat>();
        Apply(wide, filter);
        Apply(floats, filter);

        // The same filter on fractions: within single-precision rounding, far below an 8-bit unit
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                Assert.InRange(Math.Abs((floats.Frames[0][x, y].R * 65535) - wide.Frames[0][x, y].R), 0, 1.5);
                Assert.InRange(Math.Abs((floats.Frames[0][x, y].B * 65535) - wide.Frames[0][x, y].B), 0, 1.5);
            }
        }
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("guided")]
    [InlineData("zoom")]
    [InlineData("spin")]
    public void Filters_DoNotClipHighDynamicRangeSamples(string filter)
    {
        // A highlight of 4 times the reference white next to a dark area
        using var image = new Image<RgbaFloat>(20, 12);
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                image.Frames[0][x, y] = x < 10 ? new RgbaFloat(4f, 3.5f, 2f) : new RgbaFloat(0.1f, 0.1f, 0.1f);
            }
        }

        Apply(image, filter);

        Assert.InRange(image.Frames[0][1, 6].R, 3.9f, 4.1f);
        Assert.InRange(image.Frames[0][1, 6].G, 3.4f, 3.6f);
        Assert.Equal(1f, image.Frames[0][1, 6].A);
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("zoom")]
    [InlineData("spin")]
    public void Filters_DoNotDependOnTheWorkerCount(string filter)
    {
        // 300x256 = 76,800 pixels: enough for several workers
        byte[]? reference = null;
        foreach (var workers in new[] { 1, 2, 3, 8 })
        {
            using var image = new Image<Rgba32>(300, 256, new ImageConfiguration { MaxDegreeOfParallelism = workers });
            for (var y = 0; y < 256; y++)
            {
                for (var x = 0; x < 300; x++)
                {
                    image.Frames[0][x, y] = new Rgba32((byte)(x * 7), (byte)((y * 13) ^ x), (byte)(x + y), (byte)(255 - (x % 7)));
                }
            }

            Apply(image, filter);
            var bytes = new byte[300 * 256 * 4];
            image.Frames[0].CopyPixelBytesTo(bytes);
            if (reference is null)
            {
                reference = bytes;
            }
            else
            {
                Assert.Equal(reference, bytes);
            }
        }
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("guided")]
    [InlineData("zoom")]
    public void Filters_ReportTheAllocationLimitBeforeAnyChange(string filter)
    {
        // The pixels fit, the working copy (16 bytes per pixel) does not
        using var image = new Image<Rgba32>(64, 64, new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 64 * 64 * 4 * 3 } });
        image.Frames[0][5, 5] = new Rgba32(255, 255, 255, 255);
        Assert.Throws<ImageResourceLimitException>(() => Apply(image, filter));
        Assert.Equal(new Rgba32(255, 255, 255, 255), image.Frames[0][5, 5]);
        Assert.Equal(default, image.Frames[0][5, 6]);
    }

    [Theory]
    [InlineData("bilateral")]
    [InlineData("guided")]
    [InlineData("zoom")]
    public void Filters_ObserveCancellation(string filter)
    {
        using var image = CreateImage<Rgb24>(3);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => Apply(image, filter, source.Token));
    }

    [Fact]
    public void Filters_ChangeOnlyTheFrameTheyAreGiven()
    {
        using var image = CreateImage<Rgb24>(3);
        image.AppendFrame(image.Frames[0]);
        using var original = image.Clone();
        image.Frames[1].BilateralFilter(2, 1.5, 0.2, cancellationToken: Ct);
        image.Frames[1].GuidedFilter(2, 0.01, cancellationToken: Ct);
        image.Frames[1].RadialBlur(new RadialBlurOptions(RadialBlurKind.Spin, new PointD(0, 0), 10), Ct);
        Assert.Equal(original.Frames[0][3, 3], image.Frames[0][3, 3]);
        Assert.Equal(original.Frames[0][20, 10], image.Frames[0][20, 10]);
        Assert.NotEqual(ReadBytes(original.Frames[1]), ReadBytes(image.Frames[1]));
    }

    [Fact]
    public void Filters_WithRadiusZeroChangeNothing()
    {
        using var image = CreateImage<Rgb24>(3);
        var before = ReadBytes(image.Frames[0]);
        image.BilateralFilter(0, 1, 0.1, cancellationToken: Ct);
        image.GuidedFilter(0, 0.01, cancellationToken: Ct);
        image.Frames[0].BilateralFilter(0, 1, 0.1, cancellationToken: Ct);
        image.Frames[0].GuidedFilter(0, 0.01, cancellationToken: Ct);
        Assert.Equal(before, ReadBytes(image.Frames[0]));
    }

    [Fact]
    public void RadialBlur_AveragesLinearLightOnRequest()
    {
        // A checkerboard of black and white pixels: averaged as stored, it is mid-gray (128); averaged as light, the
        // sRGB value of half the light (188)
        using var encoded = new Image<Rgb24>(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                encoded.Frames[0][x, y] = (x + y) % 2 == 0 ? new Rgb24(0, 0, 0) : new Rgb24(255, 255, 255);
            }
        }

        using var linear = encoded.Clone();
        var options = new RadialBlurOptions(RadialBlurKind.Spin, new PointD(32, -2000), 2) { MaxSampleCount = 128 };
        encoded.RadialBlur(options, Ct);
        linear.RadialBlur(new RadialBlurOptions(RadialBlurKind.Spin, new PointD(32, -2000), 2) { MaxSampleCount = 128, WorkingSpace = ConvolutionWorkingSpace.LinearSrgb }, Ct);

        Assert.InRange(encoded.Frames[0][32, 32].R, 120, 136);
        Assert.InRange(linear.Frames[0][32, 32].R, 180, 196);
    }

    [Fact]
    public void Filters_ValidateTheirArguments()
    {
        using var image = CreateImage<Rgb24>(3);
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).BilateralFilter(1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>("radius", () => image.BilateralFilter(-1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>("radius", () => image.BilateralFilter(101, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>("spatialSigma", () => image.BilateralFilter(1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>("spatialSigma", () => image.BilateralFilter(1, double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>("rangeSigma", () => image.BilateralFilter(1, 1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>("edgeMode", () => image.BilateralFilter(1, 1, 1, (ConvolutionEdgeMode)42));
        Assert.Throws<ArgumentNullException>("frame", () => ((ImageFrame)null!).GuidedFilter(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>("radius", () => image.GuidedFilter(1001, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>("epsilon", () => image.GuidedFilter(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>("epsilon", () => image.Frames[0].GuidedFilter(1, -1));
        Assert.Throws<ArgumentNullException>("options", () => image.RadialBlur(null!));
        Assert.Throws<ArgumentOutOfRangeException>("amount", () => new RadialBlurOptions(RadialBlurKind.Zoom, default, 2.5));
        Assert.Throws<ArgumentOutOfRangeException>("amount", () => new RadialBlurOptions(RadialBlurKind.Zoom, default, 0));
        Assert.Throws<ArgumentOutOfRangeException>("amount", () => new RadialBlurOptions(RadialBlurKind.Spin, default, 361));
        Assert.Throws<ArgumentOutOfRangeException>("center", () => new RadialBlurOptions(RadialBlurKind.Spin, new PointD(double.NaN, 0), 10));
        Assert.Throws<ArgumentOutOfRangeException>("center", () => new RadialBlurOptions(RadialBlurKind.Spin, new PointD(0, 2e6), 10));
        Assert.Throws<ArgumentOutOfRangeException>("kind", () => new RadialBlurOptions((RadialBlurKind)7, default, 10));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadialBlurOptions(RadialBlurKind.Spin, default, 10) { MaxSampleCount = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadialBlurOptions(RadialBlurKind.Spin, default, 10) { MaxSampleCount = RadialBlurOptions.MaxSampleCountLimit + 1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadialBlurOptions(RadialBlurKind.Spin, default, 10) { EdgeMode = (ConvolutionEdgeMode)9 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadialBlurOptions(RadialBlurKind.Spin, default, 10) { WorkingSpace = (ConvolutionWorkingSpace)9 });
    }

    [Fact]
    public void Filters_RejectDisposedImages()
    {
        var image = CreateImage<Rgb24>(3);
        var frame = image.Frames[0];
        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => image.BilateralFilter(1, 1, 0.1));
        Assert.Throws<ObjectDisposedException>(() => image.GuidedFilter(1, 0.1));
        Assert.Throws<ObjectDisposedException>(() => image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Zoom, default, 0.2)));
        Assert.Throws<ObjectDisposedException>(() => frame.BilateralFilter(1, 1, 0.1));
    }

    private static void Apply(Image image, string filter, CancellationToken cancellationToken = default)
    {
        var token = cancellationToken.CanBeCanceled ? cancellationToken : Ct;
        switch (filter)
        {
            case "bilateral":
                image.BilateralFilter(2, 1.5, 0.1, ConvolutionEdgeMode.Clamp, token);
                break;
            case "guided":
                image.GuidedFilter(2, 0.01, ConvolutionEdgeMode.Mirror, token);
                break;
            case "zoom":
                image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Zoom, new PointD(image.Width / 3.0, image.Height / 2.0), 0.3), token);
                break;
            case "spin":
                image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Spin, new PointD(image.Width / 2.0, image.Height / 3.0), 25), token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter, message: null);
        }
    }

    /// <summary>
    /// Creates the image of the references: per color sample, a ramp of 64 levels with a step of 128 at <c>x = 12</c>, plus the
    /// noise of a linear congruential generator (-16 to +15); with alpha, the first 5 rows have an alpha ramp.
    /// </summary>
    private static Image<TPixel> CreateImage<TPixel>(int channels)
        where TPixel : unmanaged
    {
        var samples = CreateSamples(channels);
        using var source = new Image<Rgba32>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * channels;
                source.Frames[0][x, y] = channels switch
                {
                    1 => new Rgba32(samples[offset], samples[offset], samples[offset], 255),
                    3 => new Rgba32(samples[offset], samples[offset + 1], samples[offset + 2], 255),
                    _ => new Rgba32(samples[offset], samples[offset + 1], samples[offset + 2], samples[offset + 3]),
                };
            }
        }

        return source.CloneAs<TPixel>();
    }

    private static byte[] CreateSamples(int channels)
    {
        var samples = new byte[Width * Height * channels];
        long state = 12345;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * channels;
                for (var c = 0; c < Math.Min(channels, 3); c++)
                {
                    state = ((1103515245 * state) + 12345) % (1L << 31);
                    var noise = (int)((state >> 16) & 255);
                    var value = (((x * 7) + (y * 5) + (c * 40)) % 64) + (x >= 12 ? 128 : 0) + (noise >> 3) - 16;
                    samples[offset + c] = (byte)Math.Clamp(value, 0, 255);
                }

                if (channels == 4)
                {
                    samples[offset + 3] = (byte)(y < 5 ? (x * 11) & 255 : 255);
                }
            }
        }

        return samples;
    }

    private static byte[] ReadBytes(ImageFrame frame)
    {
        var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static List<TPixel> ReadPixels<TPixel>(Image<TPixel> image)
        where TPixel : unmanaged
    {
        var pixels = new List<TPixel>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                pixels.Add(image.Frames[0][x, y]);
            }
        }

        return pixels;
    }
}
