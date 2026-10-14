#===============================================================================
# Compatible UTF-8 display text from binary game data
#===============================================================================

module Ironmon
  def self.compatible_display_text(text)
    return text if !text.is_a?(String) ||
      text.encoding != Encoding::ASCII_8BIT || text.ascii_only?
    utf8 = text.dup.force_encoding(Encoding::UTF_8)
    return utf8.valid_encoding? ? utf8 : text
  end
end

def _INTL(*arg)
  begin
    string = MessageTypes.getFromHash(MessageTypes::ScriptTexts, arg[0])
  rescue StandardError
    string = arg[0]
  end
  string = Ironmon.compatible_display_text(string).clone
  for i in 1...arg.length
    replacement = Ironmon.compatible_display_text("#{arg[i]}")
    string.gsub!(/\{#{i}\}/, replacement)
  end
  return string
end
