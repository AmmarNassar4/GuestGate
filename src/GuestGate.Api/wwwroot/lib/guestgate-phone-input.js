(function () {
  'use strict';

  const instances = new WeakMap();
  const inputInstances = new WeakMap();
  const roots = new Set();
  let cssInjected = false;
  let documentClickAttached = false;

  const PHONE_KEYWORDS = [
    'mobile', 'phone', 'telephone', 'tel', 'cell', 'cellphone', 'whatsapp',
    'موبايل', 'الموبايل', 'جوال', 'الجوال', 'هاتف', 'الهاتف', 'تليفون', 'تلفون', 'محمول', 'رقم الجوال', 'رقم الهاتف'
  ];

  // Offline copy based on smart_phone_input_all_countries.html.
  // len = expected national number length (most common); 0 = variable.
  const countries = [
      {code:'AF', name:'أفغانستان', en:'Afghanistan', dial:'93', flag:'🇦🇫', len:9},
      {code:'AL', name:'ألبانيا', en:'Albania', dial:'355', flag:'🇦🇱', len:9},
      {code:'DZ', name:'الجزائر', en:'Algeria', dial:'213', flag:'🇩🇿', len:9},
      {code:'AS', name:'ساموا الأمريكية', en:'American Samoa', dial:'1684', flag:'🇦🇸', len:7},
      {code:'AD', name:'أندورا', en:'Andorra', dial:'376', flag:'🇦🇩', len:6},
      {code:'AO', name:'أنغولا', en:'Angola', dial:'244', flag:'🇦🇴', len:9},
      {code:'AI', name:'أنغويلا', en:'Anguilla', dial:'1264', flag:'🇦🇮', len:7},
      {code:'AG', name:'أنتيغوا وبربودا', en:'Antigua and Barbuda', dial:'1268', flag:'🇦🇬', len:7},
      {code:'AR', name:'الأرجنتين', en:'Argentina', dial:'54', flag:'🇦🇷', len:10},
      {code:'AM', name:'أرمينيا', en:'Armenia', dial:'374', flag:'🇦🇲', len:8},
      {code:'AW', name:'أروبا', en:'Aruba', dial:'297', flag:'🇦🇼', len:7},
      {code:'AU', name:'أستراليا', en:'Australia', dial:'61', flag:'🇦🇺', len:9},
      {code:'AT', name:'النمسا', en:'Austria', dial:'43', flag:'🇦🇹', len:11},
      {code:'AZ', name:'أذربيجان', en:'Azerbaijan', dial:'994', flag:'🇦🇿', len:9},
      {code:'BS', name:'الباهاما', en:'Bahamas', dial:'1242', flag:'🇧🇸', len:7},
      {code:'BH', name:'البحرين', en:'Bahrain', dial:'973', flag:'🇧🇭', len:8},
      {code:'BD', name:'بنغلاديش', en:'Bangladesh', dial:'880', flag:'🇧🇩', len:10},
      {code:'BB', name:'باربادوس', en:'Barbados', dial:'1246', flag:'🇧🇧', len:7},
      {code:'BY', name:'بيلاروس', en:'Belarus', dial:'375', flag:'🇧🇾', len:9},
      {code:'BE', name:'بلجيكا', en:'Belgium', dial:'32', flag:'🇧🇪', len:9},
      {code:'BZ', name:'بليز', en:'Belize', dial:'501', flag:'🇧🇿', len:7},
      {code:'BJ', name:'بنين', en:'Benin', dial:'229', flag:'🇧🇯', len:8},
      {code:'BM', name:'برمودا', en:'Bermuda', dial:'1441', flag:'🇧🇲', len:7},
      {code:'BT', name:'بوتان', en:'Bhutan', dial:'975', flag:'🇧🇹', len:8},
      {code:'BO', name:'بوليفيا', en:'Bolivia', dial:'591', flag:'🇧🇴', len:8},
      {code:'BA', name:'البوسنة والهرسك', en:'Bosnia and Herzegovina', dial:'387', flag:'🇧🇦', len:8},
      {code:'BW', name:'بوتسوانا', en:'Botswana', dial:'267', flag:'🇧🇼', len:8},
      {code:'BR', name:'البرازيل', en:'Brazil', dial:'55', flag:'🇧🇷', len:11},
      {code:'IO', name:'إقليم المحيط الهندي البريطاني', en:'British Indian Ocean Territory', dial:'246', flag:'🇮🇴', len:7},
      {code:'VG', name:'جزر العذراء البريطانية', en:'British Virgin Islands', dial:'1284', flag:'🇻🇬', len:7},
      {code:'BN', name:'بروناي', en:'Brunei', dial:'673', flag:'🇧🇳', len:7},
      {code:'BG', name:'بلغاريا', en:'Bulgaria', dial:'359', flag:'🇧🇬', len:9},
      {code:'BF', name:'بوركينا فاسو', en:'Burkina Faso', dial:'226', flag:'🇧🇫', len:8},
      {code:'BI', name:'بوروندي', en:'Burundi', dial:'257', flag:'🇧🇮', len:8},
      {code:'KH', name:'كمبوديا', en:'Cambodia', dial:'855', flag:'🇰🇭', len:9},
      {code:'CM', name:'الكاميرون', en:'Cameroon', dial:'237', flag:'🇨🇲', len:9},
      {code:'CA', name:'كندا', en:'Canada', dial:'1', flag:'🇨🇦', len:10},
      {code:'CV', name:'الرأس الأخضر', en:'Cape Verde', dial:'238', flag:'🇨🇻', len:7},
      {code:'KY', name:'جزر كايمان', en:'Cayman Islands', dial:'1345', flag:'🇰🇾', len:7},
      {code:'CF', name:'جمهورية أفريقيا الوسطى', en:'Central African Republic', dial:'236', flag:'🇨🇫', len:8},
      {code:'TD', name:'تشاد', en:'Chad', dial:'235', flag:'🇹🇩', len:8},
      {code:'CL', name:'تشيلي', en:'Chile', dial:'56', flag:'🇨🇱', len:9},
      {code:'CN', name:'الصين', en:'China', dial:'86', flag:'🇨🇳', len:11},
      {code:'CO', name:'كولومبيا', en:'Colombia', dial:'57', flag:'🇨🇴', len:10},
      {code:'KM', name:'جزر القمر', en:'Comoros', dial:'269', flag:'🇰🇲', len:7},
      {code:'CG', name:'الكونغو', en:'Congo', dial:'242', flag:'🇨🇬', len:9},
      {code:'CD', name:'الكونغو الديمقراطية', en:'DR Congo', dial:'243', flag:'🇨🇩', len:9},
      {code:'CK', name:'جزر كوك', en:'Cook Islands', dial:'682', flag:'🇨🇰', len:5},
      {code:'CR', name:'كوستاريكا', en:'Costa Rica', dial:'506', flag:'🇨🇷', len:8},
      {code:'CI', name:'ساحل العاج', en:'Ivory Coast', dial:'225', flag:'🇨🇮', len:10},
      {code:'HR', name:'كرواتيا', en:'Croatia', dial:'385', flag:'🇭🇷', len:9},
      {code:'CU', name:'كوبا', en:'Cuba', dial:'53', flag:'🇨🇺', len:8},
      {code:'CW', name:'كوراساو', en:'Curaçao', dial:'599', flag:'🇨🇼', len:7},
      {code:'CY', name:'قبرص', en:'Cyprus', dial:'357', flag:'🇨🇾', len:8},
      {code:'CZ', name:'التشيك', en:'Czech Republic', dial:'420', flag:'🇨🇿', len:9},
      {code:'DK', name:'الدنمارك', en:'Denmark', dial:'45', flag:'🇩🇰', len:8},
      {code:'DJ', name:'جيبوتي', en:'Djibouti', dial:'253', flag:'🇩🇯', len:8},
      {code:'DM', name:'دومينيكا', en:'Dominica', dial:'1767', flag:'🇩🇲', len:7},
      {code:'DO', name:'جمهورية الدومينيكان', en:'Dominican Republic', dial:'1809', flag:'🇩🇴', len:7},
      {code:'EC', name:'الإكوادور', en:'Ecuador', dial:'593', flag:'🇪🇨', len:9},
      {code:'EG', name:'مصر', en:'Egypt', dial:'20', flag:'🇪🇬', len:10},
      {code:'SV', name:'السلفادور', en:'El Salvador', dial:'503', flag:'🇸🇻', len:8},
      {code:'GQ', name:'غينيا الاستوائية', en:'Equatorial Guinea', dial:'240', flag:'🇬🇶', len:9},
      {code:'ER', name:'إريتريا', en:'Eritrea', dial:'291', flag:'🇪🇷', len:7},
      {code:'EE', name:'إستونيا', en:'Estonia', dial:'372', flag:'🇪🇪', len:8},
      {code:'SZ', name:'إسواتيني', en:'Eswatini', dial:'268', flag:'🇸🇿', len:8},
      {code:'ET', name:'إثيوبيا', en:'Ethiopia', dial:'251', flag:'🇪🇹', len:9},
      {code:'FK', name:'جزر فوكلاند', en:'Falkland Islands', dial:'500', flag:'🇫🇰', len:5},
      {code:'FO', name:'جزر فارو', en:'Faroe Islands', dial:'298', flag:'🇫🇴', len:6},
      {code:'FJ', name:'فيجي', en:'Fiji', dial:'679', flag:'🇫🇯', len:7},
      {code:'FI', name:'فنلندا', en:'Finland', dial:'358', flag:'🇫🇮', len:9},
      {code:'FR', name:'فرنسا', en:'France', dial:'33', flag:'🇫🇷', len:9},
      {code:'GF', name:'غويانا الفرنسية', en:'French Guiana', dial:'594', flag:'🇬🇫', len:9},
      {code:'PF', name:'بولينيزيا الفرنسية', en:'French Polynesia', dial:'689', flag:'🇵🇫', len:8},
      {code:'GA', name:'الغابون', en:'Gabon', dial:'241', flag:'🇬🇦', len:7},
      {code:'GM', name:'غامبيا', en:'Gambia', dial:'220', flag:'🇬🇲', len:7},
      {code:'GE', name:'جورجيا', en:'Georgia', dial:'995', flag:'🇬🇪', len:9},
      {code:'DE', name:'ألمانيا', en:'Germany', dial:'49', flag:'🇩🇪', len:11},
      {code:'GH', name:'غانا', en:'Ghana', dial:'233', flag:'🇬🇭', len:9},
      {code:'GI', name:'جبل طارق', en:'Gibraltar', dial:'350', flag:'🇬🇮', len:8},
      {code:'GR', name:'اليونان', en:'Greece', dial:'30', flag:'🇬🇷', len:10},
      {code:'GL', name:'غرينلاند', en:'Greenland', dial:'299', flag:'🇬🇱', len:6},
      {code:'GD', name:'غرينادا', en:'Grenada', dial:'1473', flag:'🇬🇩', len:7},
      {code:'GP', name:'غوادلوب', en:'Guadeloupe', dial:'590', flag:'🇬🇵', len:9},
      {code:'GU', name:'غوام', en:'Guam', dial:'1671', flag:'🇬🇺', len:7},
      {code:'GT', name:'غواتيمالا', en:'Guatemala', dial:'502', flag:'🇬🇹', len:8},
      {code:'GN', name:'غينيا', en:'Guinea', dial:'224', flag:'🇬🇳', len:9},
      {code:'GW', name:'غينيا بيساو', en:'Guinea-Bissau', dial:'245', flag:'🇬🇼', len:9},
      {code:'GY', name:'غيانا', en:'Guyana', dial:'592', flag:'🇬🇾', len:7},
      {code:'HT', name:'هايتي', en:'Haiti', dial:'509', flag:'🇭🇹', len:8},
      {code:'HN', name:'هندوراس', en:'Honduras', dial:'504', flag:'🇭🇳', len:8},
      {code:'HK', name:'هونغ كونغ', en:'Hong Kong', dial:'852', flag:'🇭🇰', len:8},
      {code:'HU', name:'المجر', en:'Hungary', dial:'36', flag:'🇭🇺', len:9},
      {code:'IS', name:'آيسلندا', en:'Iceland', dial:'354', flag:'🇮🇸', len:7},
      {code:'IN', name:'الهند', en:'India', dial:'91', flag:'🇮🇳', len:10},
      {code:'ID', name:'إندونيسيا', en:'Indonesia', dial:'62', flag:'🇮🇩', len:10},
      {code:'IR', name:'إيران', en:'Iran', dial:'98', flag:'🇮🇷', len:10},
      {code:'IQ', name:'العراق', en:'Iraq', dial:'964', flag:'🇮🇶', len:10},
      {code:'IE', name:'أيرلندا', en:'Ireland', dial:'353', flag:'🇮🇪', len:9},
      {code:'IL', name:'إسرائيل', en:'Israel', dial:'972', flag:'🇮🇱', len:9},
      {code:'IT', name:'إيطاليا', en:'Italy', dial:'39', flag:'🇮🇹', len:10},
      {code:'JM', name:'جامايكا', en:'Jamaica', dial:'1876', flag:'🇯🇲', len:7},
      {code:'JP', name:'اليابان', en:'Japan', dial:'81', flag:'🇯🇵', len:10},
      {code:'JO', name:'الأردن', en:'Jordan', dial:'962', flag:'🇯🇴', len:9},
      {code:'KZ', name:'كازاخستان', en:'Kazakhstan', dial:'7', flag:'🇰🇿', len:10},
      {code:'KE', name:'كينيا', en:'Kenya', dial:'254', flag:'🇰🇪', len:9},
      {code:'KI', name:'كيريباتي', en:'Kiribati', dial:'686', flag:'🇰🇮', len:5},
      {code:'XK', name:'كوسوفو', en:'Kosovo', dial:'383', flag:'🇽🇰', len:8},
      {code:'KW', name:'الكويت', en:'Kuwait', dial:'965', flag:'🇰🇼', len:8},
      {code:'KG', name:'قيرغيزستان', en:'Kyrgyzstan', dial:'996', flag:'🇰🇬', len:9},
      {code:'LA', name:'لاوس', en:'Laos', dial:'856', flag:'🇱🇦', len:10},
      {code:'LV', name:'لاتفيا', en:'Latvia', dial:'371', flag:'🇱🇻', len:8},
      {code:'LB', name:'لبنان', en:'Lebanon', dial:'961', flag:'🇱🇧', len:8},
      {code:'LS', name:'ليسوتو', en:'Lesotho', dial:'266', flag:'🇱🇸', len:8},
      {code:'LR', name:'ليبيريا', en:'Liberia', dial:'231', flag:'🇱🇷', len:8},
      {code:'LY', name:'ليبيا', en:'Libya', dial:'218', flag:'🇱🇾', len:9},
      {code:'LI', name:'ليختنشتاين', en:'Liechtenstein', dial:'423', flag:'🇱🇮', len:7},
      {code:'LT', name:'ليتوانيا', en:'Lithuania', dial:'370', flag:'🇱🇹', len:8},
      {code:'LU', name:'لوكسمبورغ', en:'Luxembourg', dial:'352', flag:'🇱🇺', len:9},
      {code:'MO', name:'ماكاو', en:'Macau', dial:'853', flag:'🇲🇴', len:8},
      {code:'MG', name:'مدغشقر', en:'Madagascar', dial:'261', flag:'🇲🇬', len:9},
      {code:'MW', name:'مالاوي', en:'Malawi', dial:'265', flag:'🇲🇼', len:9},
      {code:'MY', name:'ماليزيا', en:'Malaysia', dial:'60', flag:'🇲🇾', len:9},
      {code:'MV', name:'المالديف', en:'Maldives', dial:'960', flag:'🇲🇻', len:7},
      {code:'ML', name:'مالي', en:'Mali', dial:'223', flag:'🇲🇱', len:8},
      {code:'MT', name:'مالطا', en:'Malta', dial:'356', flag:'🇲🇹', len:8},
      {code:'MH', name:'جزر مارشال', en:'Marshall Islands', dial:'692', flag:'🇲🇭', len:7},
      {code:'MQ', name:'مارتينيك', en:'Martinique', dial:'596', flag:'🇲🇶', len:9},
      {code:'MR', name:'موريتانيا', en:'Mauritania', dial:'222', flag:'🇲🇷', len:8},
      {code:'MU', name:'موريشيوس', en:'Mauritius', dial:'230', flag:'🇲🇺', len:8},
      {code:'YT', name:'مايوت', en:'Mayotte', dial:'262', flag:'🇾🇹', len:9},
      {code:'MX', name:'المكسيك', en:'Mexico', dial:'52', flag:'🇲🇽', len:10},
      {code:'FM', name:'ميكرونيزيا', en:'Micronesia', dial:'691', flag:'🇫🇲', len:7},
      {code:'MD', name:'مولدوفا', en:'Moldova', dial:'373', flag:'🇲🇩', len:8},
      {code:'MC', name:'موناكو', en:'Monaco', dial:'377', flag:'🇲🇨', len:8},
      {code:'MN', name:'منغوليا', en:'Mongolia', dial:'976', flag:'🇲🇳', len:8},
      {code:'ME', name:'الجبل الأسود', en:'Montenegro', dial:'382', flag:'🇲🇪', len:8},
      {code:'MS', name:'مونتسرات', en:'Montserrat', dial:'1664', flag:'🇲🇸', len:7},
      {code:'MA', name:'المغرب', en:'Morocco', dial:'212', flag:'🇲🇦', len:9},
      {code:'MZ', name:'موزمبيق', en:'Mozambique', dial:'258', flag:'🇲🇿', len:9},
      {code:'MM', name:'ميانمار', en:'Myanmar', dial:'95', flag:'🇲🇲', len:9},
      {code:'NA', name:'ناميبيا', en:'Namibia', dial:'264', flag:'🇳🇦', len:9},
      {code:'NR', name:'ناورو', en:'Nauru', dial:'674', flag:'🇳🇷', len:7},
      {code:'NP', name:'نيبال', en:'Nepal', dial:'977', flag:'🇳🇵', len:10},
      {code:'NL', name:'هولندا', en:'Netherlands', dial:'31', flag:'🇳🇱', len:9},
      {code:'NC', name:'كاليدونيا الجديدة', en:'New Caledonia', dial:'687', flag:'🇳🇨', len:6},
      {code:'NZ', name:'نيوزيلندا', en:'New Zealand', dial:'64', flag:'🇳🇿', len:9},
      {code:'NI', name:'نيكاراغوا', en:'Nicaragua', dial:'505', flag:'🇳🇮', len:8},
      {code:'NE', name:'النيجر', en:'Niger', dial:'227', flag:'🇳🇪', len:8},
      {code:'NG', name:'نيجيريا', en:'Nigeria', dial:'234', flag:'🇳🇬', len:10},
      {code:'NU', name:'نيوي', en:'Niue', dial:'683', flag:'🇳🇺', len:4},
      {code:'KP', name:'كوريا الشمالية', en:'North Korea', dial:'850', flag:'🇰🇵', len:10},
      {code:'MK', name:'مقدونيا الشمالية', en:'North Macedonia', dial:'389', flag:'🇲🇰', len:8},
      {code:'NO', name:'النرويج', en:'Norway', dial:'47', flag:'🇳🇴', len:8},
      {code:'OM', name:'عُمان', en:'Oman', dial:'968', flag:'🇴🇲', len:8},
      {code:'PK', name:'باكستان', en:'Pakistan', dial:'92', flag:'🇵🇰', len:10},
      {code:'PW', name:'بالاو', en:'Palau', dial:'680', flag:'🇵🇼', len:7},
      {code:'PS', name:'فلسطين', en:'Palestine', dial:'970', flag:'🇵🇸', len:9},
      {code:'PA', name:'بنما', en:'Panama', dial:'507', flag:'🇵🇦', len:8},
      {code:'PG', name:'بابوا غينيا الجديدة', en:'Papua New Guinea', dial:'675', flag:'🇵🇬', len:8},
      {code:'PY', name:'باراغواي', en:'Paraguay', dial:'595', flag:'🇵🇾', len:9},
      {code:'PE', name:'بيرو', en:'Peru', dial:'51', flag:'🇵🇪', len:9},
      {code:'PH', name:'الفلبين', en:'Philippines', dial:'63', flag:'🇵🇭', len:10},
      {code:'PL', name:'بولندا', en:'Poland', dial:'48', flag:'🇵🇱', len:9},
      {code:'PT', name:'البرتغال', en:'Portugal', dial:'351', flag:'🇵🇹', len:9},
      {code:'PR', name:'بورتوريكو', en:'Puerto Rico', dial:'1787', flag:'🇵🇷', len:7},
      {code:'QA', name:'قطر', en:'Qatar', dial:'974', flag:'🇶🇦', len:8},
      {code:'RE', name:'لا ريونيون', en:'Réunion', dial:'262', flag:'🇷🇪', len:9},
      {code:'RO', name:'رومانيا', en:'Romania', dial:'40', flag:'🇷🇴', len:9},
      {code:'RU', name:'روسيا', en:'Russia', dial:'7', flag:'🇷🇺', len:10},
      {code:'RW', name:'رواندا', en:'Rwanda', dial:'250', flag:'🇷🇼', len:9},
      {code:'BL', name:'سان بارتيلمي', en:'Saint Barthélemy', dial:'590', flag:'🇧🇱', len:9},
      {code:'SH', name:'سانت هيلينا', en:'Saint Helena', dial:'290', flag:'🇸🇭', len:4},
      {code:'KN', name:'سانت كيتس ونيفيس', en:'Saint Kitts and Nevis', dial:'1869', flag:'🇰🇳', len:7},
      {code:'LC', name:'سانت لوسيا', en:'Saint Lucia', dial:'1758', flag:'🇱🇨', len:7},
      {code:'MF', name:'سانت مارتن', en:'Saint Martin', dial:'590', flag:'🇲🇫', len:9},
      {code:'PM', name:'سان بيير وميكلون', en:'Saint Pierre and Miquelon', dial:'508', flag:'🇵🇲', len:6},
      {code:'VC', name:'سانت فينسنت', en:'Saint Vincent', dial:'1784', flag:'🇻🇨', len:7},
      {code:'WS', name:'ساموا', en:'Samoa', dial:'685', flag:'🇼🇸', len:7},
      {code:'SM', name:'سان مارينو', en:'San Marino', dial:'378', flag:'🇸🇲', len:10},
      {code:'ST', name:'ساو تومي', en:'São Tomé and Príncipe', dial:'239', flag:'🇸🇹', len:7},
      {code:'SA', name:'السعودية', en:'Saudi Arabia', dial:'966', flag:'🇸🇦', len:9},
      {code:'SN', name:'السنغال', en:'Senegal', dial:'221', flag:'🇸🇳', len:9},
      {code:'RS', name:'صربيا', en:'Serbia', dial:'381', flag:'🇷🇸', len:9},
      {code:'SC', name:'سيشل', en:'Seychelles', dial:'248', flag:'🇸🇨', len:7},
      {code:'SL', name:'سيراليون', en:'Sierra Leone', dial:'232', flag:'🇸🇱', len:8},
      {code:'SG', name:'سنغافورة', en:'Singapore', dial:'65', flag:'🇸🇬', len:8},
      {code:'SX', name:'سينت مارتن', en:'Sint Maarten', dial:'1721', flag:'🇸🇽', len:7},
      {code:'SK', name:'سلوفاكيا', en:'Slovakia', dial:'421', flag:'🇸🇰', len:9},
      {code:'SI', name:'سلوفينيا', en:'Slovenia', dial:'386', flag:'🇸🇮', len:8},
      {code:'SB', name:'جزر سليمان', en:'Solomon Islands', dial:'677', flag:'🇸🇧', len:5},
      {code:'SO', name:'الصومال', en:'Somalia', dial:'252', flag:'🇸🇴', len:8},
      {code:'ZA', name:'جنوب أفريقيا', en:'South Africa', dial:'27', flag:'🇿🇦', len:9},
      {code:'KR', name:'كوريا الجنوبية', en:'South Korea', dial:'82', flag:'🇰🇷', len:10},
      {code:'SS', name:'جنوب السودان', en:'South Sudan', dial:'211', flag:'🇸🇸', len:9},
      {code:'ES', name:'إسبانيا', en:'Spain', dial:'34', flag:'🇪🇸', len:9},
      {code:'LK', name:'سريلانكا', en:'Sri Lanka', dial:'94', flag:'🇱🇰', len:9},
      {code:'SD', name:'السودان', en:'Sudan', dial:'249', flag:'🇸🇩', len:9},
      {code:'SR', name:'سورينام', en:'Suriname', dial:'597', flag:'🇸🇷', len:7},
      {code:'SE', name:'السويد', en:'Sweden', dial:'46', flag:'🇸🇪', len:9},
      {code:'CH', name:'سويسرا', en:'Switzerland', dial:'41', flag:'🇨🇭', len:9},
      {code:'SY', name:'سوريا', en:'Syria', dial:'963', flag:'🇸🇾', len:9},
      {code:'TW', name:'تايوان', en:'Taiwan', dial:'886', flag:'🇹🇼', len:9},
      {code:'TJ', name:'طاجيكستان', en:'Tajikistan', dial:'992', flag:'🇹🇯', len:9},
      {code:'TZ', name:'تنزانيا', en:'Tanzania', dial:'255', flag:'🇹🇿', len:9},
      {code:'TH', name:'تايلاند', en:'Thailand', dial:'66', flag:'🇹🇭', len:9},
      {code:'TL', name:'تيمور الشرقية', en:'Timor-Leste', dial:'670', flag:'🇹🇱', len:8},
      {code:'TG', name:'توغو', en:'Togo', dial:'228', flag:'🇹🇬', len:8},
      {code:'TK', name:'توكيلاو', en:'Tokelau', dial:'690', flag:'🇹🇰', len:4},
      {code:'TO', name:'تونغا', en:'Tonga', dial:'676', flag:'🇹🇴', len:5},
      {code:'TT', name:'ترينيداد وتوباغو', en:'Trinidad and Tobago', dial:'1868', flag:'🇹🇹', len:7},
      {code:'TN', name:'تونس', en:'Tunisia', dial:'216', flag:'🇹🇳', len:8},
      {code:'TR', name:'تركيا', en:'Turkey', dial:'90', flag:'🇹🇷', len:10},
      {code:'TM', name:'تركمانستان', en:'Turkmenistan', dial:'993', flag:'🇹🇲', len:8},
      {code:'TC', name:'جزر توركس وكايكوس', en:'Turks and Caicos', dial:'1649', flag:'🇹🇨', len:7},
      {code:'TV', name:'توفالو', en:'Tuvalu', dial:'688', flag:'🇹🇻', len:5},
      {code:'UG', name:'أوغندا', en:'Uganda', dial:'256', flag:'🇺🇬', len:9},
      {code:'UA', name:'أوكرانيا', en:'Ukraine', dial:'380', flag:'🇺🇦', len:9},
      {code:'AE', name:'الإمارات', en:'UAE', dial:'971', flag:'🇦🇪', len:9},
      {code:'GB', name:'بريطانيا', en:'United Kingdom', dial:'44', flag:'🇬🇧', len:10},
      {code:'US', name:'الولايات المتحدة', en:'United States', dial:'1', flag:'🇺🇸', len:10},
      {code:'UY', name:'الأوروغواي', en:'Uruguay', dial:'598', flag:'🇺🇾', len:8},
      {code:'UZ', name:'أوزبكستان', en:'Uzbekistan', dial:'998', flag:'🇺🇿', len:9},
      {code:'VU', name:'فانواتو', en:'Vanuatu', dial:'678', flag:'🇻🇺', len:7},
      {code:'VA', name:'الفاتيكان', en:'Vatican City', dial:'379', flag:'🇻🇦', len:10},
      {code:'VE', name:'فنزويلا', en:'Venezuela', dial:'58', flag:'🇻🇪', len:10},
      {code:'VN', name:'فيتنام', en:'Vietnam', dial:'84', flag:'🇻🇳', len:9},
      {code:'VI', name:'جزر العذراء الأمريكية', en:'US Virgin Islands', dial:'1340', flag:'🇻🇮', len:7},
      {code:'WF', name:'واليس وفوتونا', en:'Wallis and Futuna', dial:'681', flag:'🇼🇫', len:6},
      {code:'YE', name:'اليمن', en:'Yemen', dial:'967', flag:'🇾🇪', len:9},
      {code:'ZM', name:'زامبيا', en:'Zambia', dial:'260', flag:'🇿🇲', len:9},
      {code:'ZW', name:'زيمبابوي', en:'Zimbabwe', dial:'263', flag:'🇿🇼', len:9},
    ];

  const DEFAULT_COUNTRY_CODE = 'SA';
  const dialMap = {};
  countries.forEach(c => { if (!dialMap[c.dial]) dialMap[c.dial] = c; });
  const maxDialLen = Math.max(...countries.map(c => c.dial.length));

  function injectCss() {
    if (cssInjected || !document.head) return;
    cssInjected = true;
    const style = document.createElement('style');
    style.id = 'guestgate-phone-input-style';
    style.textContent = `
      .gg-phone-field {
        --gg-phone-bg: var(--color-background-primary, var(--badge, #fff));
        --gg-phone-bg-soft: var(--color-background-secondary, var(--accent, #f8fafc));
        --gg-phone-text: var(--color-text-primary, var(--ink, #111827));
        --gg-phone-muted: var(--color-text-secondary, var(--muted, #6b7280));
        --gg-phone-border: var(--color-border-tertiary, var(--stroke, #d0d7e2));
        --gg-phone-focus: var(--color-border-info, #0d6efd);
        --gg-phone-focus-bg: var(--color-background-info, rgba(13, 110, 253, .12));
        --gg-phone-success: var(--color-text-success, #198754);
        --gg-phone-warning: var(--color-text-warning, #d97706);
        --gg-phone-info: var(--color-text-info, #0d6efd);
        position: relative;
        width: 100%;
        display: block;
        direction: ltr;
        margin-bottom: .35rem;
      }
      .gg-phone-wrapper {
        display: flex;
        align-items: stretch;
        width: 100%;
        min-height: 46px;
        background: var(--gg-phone-bg);
        border: 1px solid var(--gg-phone-border);
        border-radius: .75rem;
        overflow: hidden;
        transition: border-color .15s ease, box-shadow .15s ease, background .15s ease;
      }
      .gg-phone-field.is-focused .gg-phone-wrapper {
        border-color: var(--gg-phone-focus);
        box-shadow: 0 0 0 3px var(--gg-phone-focus-bg);
      }
      .gg-phone-field.readonly .gg-phone-wrapper {
        opacity: .82;
        border-style: dashed;
      }
      .gg-phone-trigger {
        display: flex;
        align-items: center;
        gap: 8px;
        min-width: 110px;
        padding: 0 12px;
        border: 0;
        border-right: 1px solid var(--gg-phone-border);
        background: transparent;
        color: var(--gg-phone-text);
        cursor: pointer;
        font: inherit;
        font-size: 15px;
        transition: background .15s ease;
      }
      .gg-phone-trigger:hover { background: var(--gg-phone-bg-soft); }
      .gg-phone-trigger:disabled { cursor: default; opacity: .7; }
      .gg-phone-flag { font-size: 18px; line-height: 1; transition: transform .18s ease; }
      .gg-phone-dial { font-weight: 600; font-variant-numeric: tabular-nums; }
      .gg-phone-chevron { color: var(--gg-phone-muted); font-size: 13px; margin-inline-start: auto; }
      .gg-phone-input {
        flex: 1 1 auto;
        min-width: 0;
        height: 46px;
        padding: 0 14px;
        border: 0 !important;
        outline: 0 !important;
        box-shadow: none !important;
        background: transparent !important;
        color: var(--gg-phone-text);
        font: inherit;
        font-size: 15px;
        direction: ltr;
      }
      .gg-phone-input::placeholder { color: var(--gg-phone-muted); opacity: .85; }
      .gg-phone-clear {
        display: none;
        align-items: center;
        justify-content: center;
        width: 38px;
        border: 0;
        background: transparent;
        color: var(--gg-phone-muted);
        cursor: pointer;
        font-size: 18px;
      }
      .gg-phone-clear.is-visible { display: flex; }
      .gg-phone-dropdown {
        display: none;
        position: absolute;
        left: 0;
        right: 0;
        top: calc(100% + 6px);
        z-index: 10000;
        max-height: 320px;
        overflow: hidden;
        flex-direction: column;
        background: var(--gg-phone-bg);
        color: var(--gg-phone-text);
        border: 1px solid var(--gg-phone-border);
        border-radius: .75rem;
        box-shadow: 0 12px 30px rgba(15, 23, 42, .16);
      }
      .gg-phone-field.is-open .gg-phone-dropdown { display: flex; }
      .gg-phone-search-row {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px 10px;
        border-bottom: 1px solid var(--gg-phone-border);
      }
      .gg-phone-search-icon { color: var(--gg-phone-muted); font-size: 14px; }
      .gg-phone-search {
        flex: 1;
        min-width: 0;
        border: 0 !important;
        outline: 0 !important;
        background: transparent !important;
        color: var(--gg-phone-text);
        box-shadow: none !important;
        font: inherit;
        font-size: 14px;
        direction: auto;
      }
      .gg-phone-list { overflow-y: auto; max-height: 270px; }
      .gg-phone-country {
        display: flex;
        align-items: center;
        gap: 10px;
        width: 100%;
        padding: 9px 12px;
        border: 0;
        background: transparent;
        color: var(--gg-phone-text);
        cursor: pointer;
        text-align: right;
        font: inherit;
        font-size: 14px;
        transition: background .1s ease;
      }
      .gg-phone-country:hover,
      .gg-phone-country.is-active { background: var(--gg-phone-bg-soft); }
      .gg-phone-country-flag { font-size: 18px; line-height: 1; }
      .gg-phone-country-name {
        flex: 1;
        min-width: 0;
        text-align: right;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .gg-phone-country-dial {
        direction: ltr;
        color: var(--gg-phone-muted);
        font-variant-numeric: tabular-nums;
      }
      .gg-phone-empty { padding: 16px; text-align: center; color: var(--gg-phone-muted); font-size: 13px; }
      .gg-phone-status {
        min-height: 18px;
        margin: 8px 0 0;
        color: var(--gg-phone-muted);
        font-size: 13px;
        direction: rtl;
        text-align: start;
      }
      .gg-phone-status.success { color: var(--gg-phone-success); }
      .gg-phone-status.warning { color: var(--gg-phone-warning); }
      .gg-phone-status.info { color: var(--gg-phone-info); }
      .gg-phone-field[data-hide-status="1"] .gg-phone-status { display: none; }
    `;
    document.head.appendChild(style);
  }

  function attachDocumentClick() {
    if (documentClickAttached) return;
    documentClickAttached = true;
    document.addEventListener('click', (event) => {
      for (const root of roots) {
        if (!root.contains(event.target)) closeDropdown(root);
      }
    });
  }

  function normalizeDigits(value) {
    const map = {
      '٠': '0', '١': '1', '٢': '2', '٣': '3', '٤': '4', '٥': '5', '٦': '6', '٧': '7', '٨': '8', '٩': '9',
      '۰': '0', '۱': '1', '۲': '2', '۳': '3', '۴': '4', '۵': '5', '۶': '6', '۷': '7', '۸': '8', '۹': '9'
    };
    return String(value || '').replace(/[٠-٩۰-۹]/g, d => map[d] || d);
  }

  function normaliseDigits(value) { return normalizeDigits(value); }

  function fieldKey(field) {
    if (!field) return '';
    return String(field.key || field.name || field.id || '');
  }

  function fieldLabel(field) {
    if (!field) return '';
    return String(field.label || field.title || fieldKey(field) || '');
  }

  function isPhoneField(field) {
    const dataType = String((field && field.dataType) || '').toLowerCase();
    if (['phone', 'tel', 'telephone', 'mobile', 'cell', 'cellphone'].includes(dataType)) return true;
    const text = [fieldKey(field), fieldLabel(field), dataType].join(' ').toLowerCase();
    return PHONE_KEYWORDS.some(k => text.includes(k.toLowerCase()));
  }

  function getDefaultCountry() {
    return countries.find(c => c.code === DEFAULT_COUNTRY_CODE) || countries[0];
  }

  function findCountryByCode(code) {
    const wanted = String(code || '').toUpperCase();
    return countries.find(c => c.code === wanted) || null;
  }

  function findInstance(element) {
    if (!element) return null;
    if (instances.has(element)) return instances.get(element);
    if (inputInstances.has(element)) return inputInstances.get(element);
    const root = element.closest && element.closest('[data-guestgate-phone="1"]');
    return root && instances.has(root) ? instances.get(root) : null;
  }

  function formatNumber(digits) {
    if (!digits) return '';
    if (digits.length <= 3) return digits;
    return digits.replace(/(\d{3})(?=\d)/g, '$1 ').trim();
  }

  function processInput(raw) {
    const normalized = normalizeDigits(raw);
    const trimmed = normalized.trimStart().replace(/[‎‏؜]/g, '');

    if (trimmed.startsWith('+') || trimmed.startsWith('00')) {
      const prefix = trimmed.startsWith('+') ? '+' : '00';
      const rest = trimmed.slice(prefix.length).replace(/\D/g, '');

      let matched = null;
      let matchedLen = 0;
      for (let len = Math.min(maxDialLen, rest.length); len >= 1; len--) {
        const candidate = rest.slice(0, len);
        if (dialMap[candidate]) {
          matched = dialMap[candidate];
          matchedLen = len;
          break;
        }
      }

      if (matched) {
        const local = rest.slice(matchedLen);
        return { display: formatNumber(local), country: matched, pendingInternational: false };
      }
      return { display: prefix + rest, country: null, pendingInternational: true };
    }

    const digits = trimmed.replace(/\D/g, '');
    return { display: formatNumber(digits), country: null, pendingInternational: false };
  }

  function setCountry(inst, country, animate) {
    if (!country) return;
    inst.currentCountry = country;
    inst.flagEl.textContent = country.flag;
    inst.dialEl.textContent = '+' + country.dial;
    if (animate) {
      inst.flagEl.style.transform = 'scale(1.25)';
      setTimeout(() => { inst.flagEl.style.transform = 'scale(1)'; }, 180);
    }
    renderList(inst, inst.searchInput.value || '');
  }

  function getNationalDigits(inst) {
    if (!inst) return '';
    let national = normalizeDigits(inst.input.value).replace(/\D/g, '');
    if (national.startsWith('0')) national = national.replace(/^0+/, '');
    return national;
  }

  function getInfo(element) {
    const inst = findInstance(element);
    if (!inst) return null;
    const national = getNationalDigits(inst);
    return {
      country: inst.currentCountry.code,
      countryName: inst.currentCountry.en,
      countryNameAr: inst.currentCountry.name,
      dial: inst.currentCountry.dial,
      national,
      e164: national ? '+' + inst.currentCountry.dial + national : '',
      isValid: !national || !inst.currentCountry.len || national.length === inst.currentCountry.len,
      expectedLength: inst.currentCountry.len || 0
    };
  }

  function getValue(element) {
    const info = getInfo(element);
    return info ? info.e164 : '';
  }

  function updateStatus(inst, mode) {
    const raw = inst.input.value.trim();
    inst.clearBtn.classList.toggle('is-visible', !!raw && !inst.readOnly);
    inst.input.setCustomValidity('');

    if (!raw) {
      inst.statusMsg.textContent = '';
      inst.statusMsg.className = 'gg-phone-status';
      return;
    }

    if (mode === 'pendingInternational') {
      inst.statusMsg.textContent = 'Recognizing country...';
      inst.statusMsg.className = 'gg-phone-status';
      return;
    }

    const national = getNationalDigits(inst);
    const expected = inst.currentCountry.len;
    if (!expected) {
      inst.statusMsg.textContent = inst.currentCountry.name;
      inst.statusMsg.className = 'gg-phone-status info';
      return;
    }

    const hadLeadingZero = normalizeDigits(raw).replace(/\D/g, '').startsWith('0');
    if (national.length === expected) {
      const note = hadLeadingZero ? ' • Leading zero will be ignored' : '';
      inst.statusMsg.textContent = '✓ Valid number • ' + inst.currentCountry.name + note;
      inst.statusMsg.className = 'gg-phone-status success';
    } else if (national.length < expected) {
      inst.statusMsg.textContent = national.length + ' / ' + expected + ' digits';
      inst.statusMsg.className = 'gg-phone-status';
    } else {
      inst.statusMsg.textContent = 'The number is longer than expected (' + expected + ' digits)';
      inst.statusMsg.className = 'gg-phone-status warning';
    }
  }

  function cleanOnBlur(inst) {
    const raw = inst.input.value.trim();
    if (!raw || raw.startsWith('+') || raw.startsWith('00')) return;
    let digits = normalizeDigits(raw).replace(/\D/g, '');
    if (digits.startsWith('0') && digits.length > 1) {
      digits = digits.replace(/^0+/, '');
      inst.input.value = formatNumber(digits);
      setCountry(inst, inst.currentCountry, true);
    }
    updateStatus(inst);
  }

  function renderList(inst, filter) {
    const f = String(filter || '').trim().toLowerCase();
    inst.listEl.textContent = '';
    const filtered = countries.filter(c =>
      !f ||
      c.name.toLowerCase().includes(f) ||
      c.en.toLowerCase().includes(f) ||
      c.dial.includes(f) ||
      c.code.toLowerCase().includes(f)
    );

    if (!filtered.length) {
      const empty = document.createElement('div');
      empty.className = 'gg-phone-empty';
      empty.textContent = 'No results found';
      inst.listEl.appendChild(empty);
      return;
    }

    const frag = document.createDocumentFragment();
    for (const c of filtered) {
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'gg-phone-country' + (c.code === inst.currentCountry.code ? ' is-active' : '');
      row.dataset.country = c.code;

      const flag = document.createElement('span');
      flag.className = 'gg-phone-country-flag';
      flag.textContent = c.flag;
      row.appendChild(flag);

      const name = document.createElement('span');
      name.className = 'gg-phone-country-name';
      name.textContent = c.name;
      row.appendChild(name);

      const dial = document.createElement('span');
      dial.className = 'gg-phone-country-dial';
      dial.textContent = '+' + c.dial;
      row.appendChild(dial);

      row.addEventListener('click', () => {
        setCountry(inst, c, true);
        closeDropdown(inst.root);
        inst.input.focus();
        updateStatus(inst);
      });
      frag.appendChild(row);
    }
    inst.listEl.appendChild(frag);
  }

  function openDropdown(root) {
    const inst = findInstance(root);
    if (!inst || inst.readOnly) return;
    for (const other of roots) { if (other !== inst.root) closeDropdown(other); }
    inst.root.classList.add('is-open');
    inst.searchInput.value = '';
    renderList(inst, '');
    setTimeout(() => inst.searchInput.focus(), 40);
  }

  function closeDropdown(root) {
    const inst = findInstance(root);
    if (!inst) return;
    inst.root.classList.remove('is-open');
  }

  function setReadOnly(element, readOnly) {
    const inst = findInstance(element);
    if (!inst) return;
    inst.readOnly = !!readOnly;
    inst.input.readOnly = !!readOnly;
    inst.trigger.disabled = !!readOnly;
    inst.clearBtn.disabled = !!readOnly;
    inst.root.classList.toggle('readonly', !!readOnly);
    if (readOnly) closeDropdown(inst.root);
    updateStatus(inst);
  }

  function setRequired(element, required) {
    const inst = findInstance(element);
    if (!inst) return;
    inst.input.required = !!required;
  }

  function setName(element, name) {
    const inst = findInstance(element);
    if (!inst) return;
    inst.input.name = String(name || '');
  }

  function setValue(element, value) {
    const inst = findInstance(element);
    if (!inst) return;
    const next = normalizeDigits(value || '');
    inst.input.value = next;
    handleInput(inst);
  }

  function handleInput(inst) {
    const raw = inst.input.value;
    inst.input.setCustomValidity('');

    if (!raw.trim()) {
      setCountry(inst, inst.defaultCountry, false);
      updateStatus(inst);
      return;
    }

    const result = processInput(raw);
    if (result.country) setCountry(inst, result.country, true);

    if (result.display !== raw) {
      inst.input.value = result.display;
      try { inst.input.setSelectionRange(inst.input.value.length, inst.input.value.length); } catch { }
    }
    updateStatus(inst, result.pendingInternational ? 'pendingInternational' : undefined);
  }

  function defineProxyProperties(root) {
    const define = (name, descriptor) => {
      try { Object.defineProperty(root, name, { configurable: true, enumerable: false, ...descriptor }); } catch { }
    };
    define('value', { get() { return getValue(root); }, set(v) { setValue(root, v); } });
    define('required', { get() { const i = findInstance(root); return !!(i && i.input.required); }, set(v) { setRequired(root, v); } });
    define('readOnly', { get() { const i = findInstance(root); return !!(i && i.input.readOnly); }, set(v) { setReadOnly(root, v); } });
    define('disabled', { get() { const i = findInstance(root); return !!(i && i.input.disabled); }, set(v) { const i = findInstance(root); if (i) { i.input.disabled = !!v; i.trigger.disabled = !!v; i.clearBtn.disabled = !!v; } } });
    define('name', { get() { const i = findInstance(root); return (i && i.input.name) || ''; }, set(v) { setName(root, v); } });
  }

  function createInput(field, value, options) {
    injectCss();
    attachDocumentClick();

    const opts = options || {};
    const key = opts.key || fieldKey(field) || ('phone_' + Math.random().toString(36).slice(2));
    const defaultCountry = findCountryByCode(opts.initialCountry || opts.country || DEFAULT_COUNTRY_CODE) || getDefaultCountry();

    const root = document.createElement('div');
    root.className = 'gg-phone-field';
    root.dataset.guestgatePhone = '1';
    root.dataset.field = key;
    root.setAttribute('dir', 'ltr');
    if (opts.hideStatus) root.dataset.hideStatus = '1';

    const wrapper = document.createElement('div');
    wrapper.className = 'gg-phone-wrapper';
    root.appendChild(wrapper);

    const trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'gg-phone-trigger';
    trigger.setAttribute('aria-label', 'Select country');
    wrapper.appendChild(trigger);

    const flagEl = document.createElement('span');
    flagEl.className = 'gg-phone-flag';
    trigger.appendChild(flagEl);

    const dialEl = document.createElement('span');
    dialEl.className = 'gg-phone-dial';
    trigger.appendChild(dialEl);

    const chev = document.createElement('span');
    chev.className = 'gg-phone-chevron';
    chev.setAttribute('aria-hidden', 'true');
    chev.textContent = '▾';
    trigger.appendChild(chev);

    const input = document.createElement('input');
    input.type = 'tel';
    input.inputMode = 'tel';
    input.autocomplete = 'tel';
    input.placeholder = opts.placeholder || 'Input phone number in any format';
    input.className = 'gg-phone-input' + (opts.className ? ' ' + opts.className : '');
    input.name = key;
    if (opts.required) input.required = true;
    if (field && field.validation) {
      if (field.validation.minLength) input.minLength = field.validation.minLength;
      if (field.validation.maxLength) input.maxLength = field.validation.maxLength;
      if (field.validation.regex) input.pattern = field.validation.regex;
    }
    wrapper.appendChild(input);

    const clearBtn = document.createElement('button');
    clearBtn.type = 'button';
    clearBtn.className = 'gg-phone-clear';
    clearBtn.setAttribute('aria-label', 'Clear');
    clearBtn.textContent = '×';
    wrapper.appendChild(clearBtn);

    const dropdown = document.createElement('div');
    dropdown.className = 'gg-phone-dropdown';
    root.appendChild(dropdown);

    const searchRow = document.createElement('div');
    searchRow.className = 'gg-phone-search-row';
    dropdown.appendChild(searchRow);

    const searchIcon = document.createElement('span');
    searchIcon.className = 'gg-phone-search-icon';
    searchIcon.setAttribute('aria-hidden', 'true');
    searchIcon.textContent = '⌕';
    searchRow.appendChild(searchIcon);

    const searchInput = document.createElement('input');
    searchInput.type = 'text';
    searchInput.className = 'gg-phone-search';
    searchInput.placeholder = 'ابحث عن دولة... / Search country';
    searchRow.appendChild(searchInput);

    const listEl = document.createElement('div');
    listEl.className = 'gg-phone-list';
    dropdown.appendChild(listEl);

    const statusMsg = document.createElement('p');
    statusMsg.className = 'gg-phone-status';
    root.appendChild(statusMsg);

    const inst = {
      root, wrapper, trigger, flagEl, dialEl, input, clearBtn, dropdown, searchInput, listEl, statusMsg,
      defaultCountry, currentCountry: defaultCountry, readOnly: false
    };
    instances.set(root, inst);
    inputInstances.set(input, inst);
    roots.add(root);
    defineProxyProperties(root);
    setCountry(inst, defaultCountry, false);
    renderList(inst, '');

    trigger.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      inst.root.classList.contains('is-open') ? closeDropdown(inst.root) : openDropdown(inst.root);
    });

    dropdown.addEventListener('click', event => event.stopPropagation());
    searchInput.addEventListener('input', () => renderList(inst, searchInput.value));

    input.addEventListener('input', () => handleInput(inst));
    input.addEventListener('focus', () => inst.root.classList.add('is-focused'));
    input.addEventListener('blur', () => {
      cleanOnBlur(inst);
      inst.root.classList.remove('is-focused');
    });

    clearBtn.addEventListener('click', () => {
      input.value = '';
      setCountry(inst, inst.defaultCountry, false);
      updateStatus(inst);
      input.focus();
    });

    if (value !== undefined && value !== null && value !== '') setValue(root, value);
    if (opts.readOnly) setReadOnly(root, true);
    return root;
  }

  function enhance(input, value, options) {
    if (!input) return null;
    if (instances.has(input) || inputInstances.has(input)) return findInstance(input);
    if (input.tagName !== 'INPUT') return null;

    const opts = options || {};
    const field = { key: input.name || input.dataset.field || opts.key || '', label: input.getAttribute('aria-label') || input.placeholder || '', dataType: 'phone' };
    const root = createInput(field, value !== undefined ? value : input.value, {
      ...opts,
      key: opts.key || input.dataset.field || input.name,
      required: opts.required !== undefined ? opts.required : input.required,
      readOnly: opts.readOnly !== undefined ? opts.readOnly : input.readOnly,
      placeholder: opts.placeholder || input.placeholder || 'Mobile number',
      className: opts.className || input.className || ''
    });

    if (input.parentNode) input.parentNode.replaceChild(root, input);
    return findInstance(root);
  }

  async function validateForm(form) {
    const scope = form || document;
    const fields = Array.from(scope.querySelectorAll('[data-guestgate-phone="1"]'));
    for (const field of fields) {
      const inst = findInstance(field);
      if (!inst) continue;
      inst.input.setCustomValidity('');
      const national = getNationalDigits(inst);
      if (inst.input.required && !national) {
        inst.input.setCustomValidity('Please enter a mobile number.');
        inst.input.reportValidity();
        return false;
      }
      if (national && inst.currentCountry.len && national.length !== inst.currentCountry.len) {
        inst.input.setCustomValidity('Please enter a valid mobile number for the selected country.');
        inst.input.reportValidity();
        updateStatus(inst);
        return false;
      }
    }
    return true;
  }

  window.GuestGatePhone = {
    countries,
    isPhoneField,
    createInput,
    enhance,
    getValue,
    getInfo,
    validateForm,
    setValue,
    setReadOnly,
    normaliseDigits,
    normalizeDigits
  };
})();
