#===============================================================================
# Ironmon local development helper
# This file must not be included in release distributions.
#===============================================================================

$DEBUG = true

Events.onMapUpdate += proc do |_sender, _event|
  $DEBUG = true
end
