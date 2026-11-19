namespace TimelineNoticeEditor
{
    public static partial class Localization
    {
        private static void AddUpdateResources()
        {
            Add(English, "CheckLimited=GitHub is limiting or refusing requests. Try again later.|UpdateLimited=Update {0} → {1} is available. GitHub API access is limited; the official ZIP and SHA-256 will be verified when you update.|UpdateMetadataFailed=Update {0} → {1} is available. Release details could not be retrieved; verified package download remains available.");
            Add(Japanese, "CheckLimited=GitHubがリクエストを制限・拒否しています。時間をおいて再確認してください。|UpdateLimited=更新があります: {0} → {1}。GitHub APIは制限中ですが、更新操作時に公式ZIPとSHA-256を検証できます。|UpdateMetadataFailed=更新があります: {0} → {1}。更新詳細の通信に失敗しましたが、検証付きダウンロードを利用できます。");
            Add(Chinese, "CheckLimited=GitHub正在限制或拒绝请求，请稍后重试。|UpdateLimited=可更新: {0} → {1}。GitHub API受限，更新时仍会验证官方ZIP和SHA-256。|UpdateMetadataFailed=可更新: {0} → {1}。无法获取更新详情，仍可下载并验证更新包。");
            Add(Korean, "CheckLimited=GitHub가 요청을 제한하거나 거부하고 있습니다. 나중에 다시 확인하세요.|UpdateLimited=업데이트 가능: {0} → {1}. GitHub API가 제한되었지만 업데이트 시 공식 ZIP과 SHA-256을 검증합니다.|UpdateMetadataFailed=업데이트 가능: {0} → {1}. 상세 정보 통신에 실패했지만 검증된 패키지 다운로드를 사용할 수 있습니다.");
        }
    }
}
