namespace IoTCoWork.Workbench.Services;

public static class ImageEditPromptBuilder
{
    public static string BuildMaskedRevisionPrompt(string instruction, string previousPrompt)
    {
        var trimmedInstruction = instruction.Trim();
        var priorPromptSection = string.IsNullOrWhiteSpace(previousPrompt)
            ? string.Empty
            : $"""

            上一轮提示词：{previousPrompt.Trim()}
            """;

        return $"""
            这是一次局部标注修图，不是重新生成整张图片。输入图片是编辑目标图，不是仅供参考的风格图。

            遮罩/标注区域是唯一允许修改的范围。只修改用户标注的遮罩区域内的内容；未标注区域必须保持原样，尽可能保持像素级一致。

            用户要修改：{trimmedInstruction}
            {priorPromptSection}

            编辑要求：标注/遮罩只用于定位，最终图片中不要保留任何标注痕迹。修改区域要和周围像素自然衔接，边缘干净，光影、材质、透视、清晰度、肤色和色彩与原图一致。

            保留约束：不要改变未标注区域；不要重新构图；不要重绘整张图；不要改变人物身份、脸部、发型、表情、身体姿势、服装、背景、灯光、镜头角度、画幅比例、照片风格或画质。

            如果修改目标是手部，请修正为自然、正常、符合人体结构的手：手指数量正确，关节清楚，手掌比例合理，手腕与手臂连接自然，手位符合原姿势和透视。

            负面约束：换人，换脸，换衣服，换背景，改变姿势，改变构图，新增人物，新增多余物体，畸形手，多指，少指，断指，粘连手指，扭曲手掌，错误关节，手腕断裂，模糊手部，脸部变形，身体比例错误，过度修饰，低质量，失真。
            """;
    }

    public static string BuildMaskedRevisionUserMessage(string instruction)
    {
        var trimmedInstruction = instruction.Trim();
        return $"""
            局部标注修图：{trimmedInstruction}

            只修改已标注/遮罩区域。未标注区域必须保持原样；不要重新生成整张图，不要改变人物、服装、背景、姿势、构图或风格。标注只用于定位，最终图中不要保留标注。
            """;
    }
}
