# 저작권 및 라이선스 안내

Script Studio는 음성을 텍스트로 바꾸는 도구입니다. 앱을 무료로 배포한다는 사실이나
PC 안에서 변환한다는 사실이 입력한 콘텐츠의 이용허락을 대신하지는 않습니다.

## 음원과 전사본을 이용할 때

- 본인이 권리를 보유한 자료, 전사 및 필요한 활용을 허락받은 자료 등 적법하게 이용할 수 있는 음원을 사용하세요.
- 직접 녹음한 파일이라도 다른 사람의 저작물이나 발언이 포함되어 있다면 필요한 이용허락과 관련 권리를 확인하세요.
- 공개 라이선스가 적용된 자료는 출처 표시, 비영리, 변경 제한 등 해당 라이선스의 조건을 따르세요.
- 음원을 받아 적거나 문장을 편집해도 원본 콘텐츠의 저작권이 자동으로 소멸하거나 사용자에게 이전되지 않습니다.
- 교재·강의·오디오북·유료 콘텐츠 등의 원본, 전사본, 본문이 읽히는 캡처를 GitHub·SNS 등에 공개하거나 재배포하기 전에는 그 이용을 허용하는 근거를 확인하세요.
- 무료 게시, 교육 목적, 출처 표기만으로 모든 이용이 허용되는 것은 아닙니다.

한국 저작권법 제30조의 사적이용을 위한 복제는 일정한 요건 아래 개인·가정 등에 한정된 범위의 이용을 규정합니다.
개인적으로 이용할 수 있는 경우라고 해서 인터넷 공개 게시까지 같은 근거로 허용되는 것은 아닙니다.
제28조의 인용 역시 이용 목적, 정당한 범위, 공정한 관행 등의 요건을 충족해야 합니다.
구체적인 이용이 허용되는지는 해당 콘텐츠의 권리관계와 이용 방식에 따라 달라집니다.

공식 근거: [저작권법 제30조](https://law.go.kr/lsLinkCommonInfo.do?chrClsCd=010202&lsJoLnkSeq=1025202705),
[저작권법 제28조](https://law.go.kr/lsLinkCommonInfo.do?chrClsCd=010202&lsJoLnkSeq=1025517657).
이 안내는 개별 콘텐츠에 대한 이용허락이나 법적 적합성 보증이 아닙니다.

## 데모 이미지와 배포 자료

README의 `docs/screenshots/` 이미지 3장은 제품 소개를 위해 작성한 영어 예문과 샘플 파일 목록을 넣은 기능 시연 화면입니다.
실제 교재의 본문, 사용자 음원에서 추출한 문장, 개인 작업 이력을 사용하지 않습니다.
화면 속 파일 목록과 문장은 기능 설명을 위한 예시이며, 특정 음원의 실제 인식 결과나 성능 수치를 제시하는 자료가 아닙니다.

현재 공식 소스 저장소와 실행 패키지는 사용자 MP3, 실제 음원에서 변환한 스크립트, `data/` 작업 이력을 포함하지 않습니다.
공개 예제나 기여 자료를 추가할 때도 공유 권한이 있는 자료만 제출하세요.

## 포함된 음성 인식 도구와 모델

| 구성 요소 | 원저작권 표시 | 라이선스 | 저장소의 원문 |
| --- | --- | --- | --- |
| whisper.cpp 실행 도구 및 포함된 ggml 라이브러리 | Copyright (c) 2023-2026 The ggml authors | MIT | [WHISPER_CPP_LICENSE.txt](https://github.com/Kay-lucas/script-studio/blob/main/licenses/WHISPER_CPP_LICENSE.txt) |
| OpenAI Whisper 영어 모델 가중치 | Copyright (c) 2022 OpenAI | MIT | [WHISPER_MODEL_LICENSE.txt](https://github.com/Kay-lucas/script-studio/blob/main/licenses/WHISPER_MODEL_LICENSE.txt) |

MIT 라이선스는 해당 구성 요소의 이용·수정·배포 등을 허용하며, 복사본이나 상당 부분을 재배포할 때 원저작권 및 허가 고지를 포함하도록 요구합니다.
라이선스 원문에 명시된 보증 부인 등의 조건도 함께 확인하세요. 위 표의 요약이 원문을 대체하지 않습니다.
배포 ZIP에는 `WHISPER_CPP_LICENSE.txt`, `WHISPER_MODEL_LICENSE.txt`, `THIRD_PARTY.txt`가 포함되어 있습니다.
버전·다운로드 출처·체크섬은 [THIRD_PARTY.txt](THIRD_PARTY.txt)를 참고하세요.

공식 출처: [whisper.cpp 라이선스](https://github.com/ggml-org/whisper.cpp/blob/master/LICENSE),
[OpenAI Whisper 라이선스](https://github.com/openai/whisper/blob/main/LICENSE),
[Whisper 코드와 모델 가중치의 라이선스 안내](https://github.com/openai/whisper#license).

이 라이선스들은 해당 도구와 모델에 적용됩니다. 사용자가 입력한 음원이나 전사본의 공개·배포 권한을 부여하지 않습니다.

## Script Studio 자체 코드의 라이선스 상태

현재 이 저장소에는 Script Studio 자체 코드 전체에 적용되는 별도의 `LICENSE` 파일을 지정하지 않았습니다.
타사 구성 요소의 MIT 라이선스가 앱의 자체 코드·문서·이미지 전체에 자동으로 적용되는 것은 아닙니다.
저장소가 공개되어 있거나 실행 앱이 무료라는 이유만으로 자체 코드의 모든 재사용·재배포 권한이 부여되었다고 해석하지 마세요.
자체 코드나 자료를 재사용·재배포하려면 적용되는 허락 범위를 저장소 관리자에게 확인하세요.

## 권리 관련 문의

이 저장소나 배포 자료에 권리 침해가 의심되는 내용이 있다면
[GitHub Issues](https://github.com/Kay-lucas/script-studio/issues)에 해당 자료의 위치와 권리 근거, 요청 내용을 알려 주세요.
공개 이슈에는 공개할 권한이 없는 원문·음원이나 민감한 개인정보를 첨부하지 마세요.
