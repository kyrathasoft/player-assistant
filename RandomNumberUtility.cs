using System.Security.Cryptography;

namespace PlayerAssistant
{
    internal static class RandomNumberUtility
    {
        public static int GenerateInteger(int minimumValue, int maximumValue)
        {
            if (minimumValue > maximumValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumValue),
                    "The minimum value must be less than or equal to the maximum value.");
            }

            var range = (ulong)((long)maximumValue - minimumValue + 1);
            var randomValueCount = 1UL << 32;
            var unbiasedLimit = randomValueCount - (randomValueCount % range);

            ulong randomValue;
            do
            {
                randomValue = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint)));
            }
            while (randomValue >= unbiasedLimit);

            return minimumValue + (int)(randomValue % range);
        }
    }
}
