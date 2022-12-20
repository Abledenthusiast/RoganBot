call az acr build --platform Windows -r acrworkers -t roganbotv1 .

call az container create --resource-group apps ^
                    --name roganbotv1 ^
                    --image acrworkers.azurecr.io/roganbotv1:latest ^
                    --registry-username acrworkers ^
                    --registry-password <insert registry password here> ^
                    --location southcentralus ^
                    --os-type Windows ^
                    --secure-environment-variables RoganBotSettings__Token=<insert token here>
